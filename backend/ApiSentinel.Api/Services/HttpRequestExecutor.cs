using System.Net.Http.Headers;
using System.Text;
using ApiSentinel.Api.Security;

namespace ApiSentinel.Api.Services;

public sealed record HttpExecutionResult(int Status, long DurationMs, string Body, Dictionary<string, string> Headers);

public sealed class HttpRequestExecutor(IHttpClientFactory clients, IConfiguration configuration, IHostEnvironment environment)
{
    public async Task<HttpExecutionResult> ExecuteAsync(string url, string method, string? body, string? contentType, Dictionary<string, string>? headers, string authenticationType, string? credential, string? authHeaderName, IReadOnlyDictionary<string, EnvironmentValue> variables, CancellationToken cancellationToken, int timeoutSeconds = 20, IReadOnlyCollection<ApiQueryParameterInput>? queryParameters = null)
    {
        if (timeoutSeconds is < 1 or > 120) throw new ArgumentException("Request timeout must be between 1 and 120 seconds.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var requestToken = timeout.Token;
        url = EnvironmentResolver.Resolve(url, variables);
        method = EnvironmentResolver.Resolve(method, variables);
        body = EnvironmentResolver.Resolve(body, variables);
        contentType = EnvironmentResolver.Resolve(contentType, variables);
        credential = EnvironmentResolver.Resolve(credential, variables);
        authHeaderName = EnvironmentResolver.Resolve(authHeaderName, variables);
        headers = EnvironmentResolver.ResolveHeaders(headers, variables);
        var resolvedQueryParameters = (queryParameters ?? []).Where(parameter => parameter.IsEnabled)
            .Select(parameter => new ApiQueryParameterInput(EnvironmentResolver.Resolve(parameter.Name, variables), EnvironmentResolver.Resolve(parameter.Value, variables)))
            .ToArray();
        if (resolvedQueryParameters.Length > 100 || resolvedQueryParameters.Any(parameter => string.IsNullOrWhiteSpace(parameter.Name) || parameter.Name.Length > 256 || parameter.Value.Length > 8192 || parameter.Name.Any(char.IsControl)))
            throw new ArgumentException("Enter up to 100 valid query parameters with names up to 256 characters and values up to 8192 characters.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)) throw new ArgumentException("Enter a valid HTTP or HTTPS URL without embedded credentials.");
        if (resolvedQueryParameters.Length > 0)
        {
            var builder = new UriBuilder(uri);
            var existingQuery = builder.Query.TrimStart('?');
            var appendedQuery = string.Join("&", resolvedQueryParameters.Select(parameter => $"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(parameter.Value)}"));
            builder.Query = string.IsNullOrEmpty(existingQuery) ? appendedQuery : $"{existingQuery}&{appendedQuery}";
            uri = builder.Uri;
        }
        var verb = method.ToUpperInvariant() switch { "GET" => HttpMethod.Get, "POST" => HttpMethod.Post, "PUT" => HttpMethod.Put, "PATCH" => HttpMethod.Patch, "DELETE" => HttpMethod.Delete, _ => throw new ArgumentException("Unsupported HTTP method.") };
        var testingLaboratoryPort = environment.IsEnvironment("Testing") ? configuration.GetValue<int?>("Outbound:TestingLaboratoryPort") : null;
        var addresses = await OutboundTargetPolicy.ResolveAllowedAsync(uri, requestToken, testingLaboratoryPort);
        using var request = new HttpRequestMessage(verb, uri);
        request.Options.Set(OutboundTargetPolicy.PinnedAddresses, addresses);
        if (!string.IsNullOrWhiteSpace(body)) request.Content = new StringContent(body, Encoding.UTF8, contentType ?? "application/json");
        if (!string.IsNullOrEmpty(credential))
        {
            switch (authenticationType)
            {
                case "Bearer": request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential); break;
                case "Basic":
                    var separator = credential.IndexOf(':');
                    if (separator < 0) throw new ArgumentException("Basic credentials must be entered as username:password.");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credential)));
                    break;
                case "ApiKey":
                    if (string.IsNullOrWhiteSpace(authHeaderName) || authHeaderName is "Host" or "Content-Length") throw new ArgumentException("Choose a valid API key header name.");
                    if (!request.Headers.TryAddWithoutValidation(authHeaderName, credential) && (request.Content is null || !request.Content.Headers.TryAddWithoutValidation(authHeaderName, credential))) throw new ArgumentException("API key header name is not valid.");
                    break;
                case "None": break;
                default: throw new ArgumentException("Unsupported authentication type.");
            }
        }
        foreach (var header in headers ?? [])
        {
            if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value) && request.Content is not null) request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        var timer = System.Diagnostics.Stopwatch.StartNew();
        using var response = await clients.CreateClient("target").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken);
        if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
        {
            var destination = location.IsAbsoluteUri ? location : new Uri(uri, location);
            await OutboundTargetPolicy.ResolveAllowedAsync(destination, requestToken, testingLaboratoryPort);
        }
        var responseBody = await ReadLimited(response.Content, requestToken);
        timer.Stop();
        var responseHeaders = response.Headers.Concat(response.Content.Headers).ToDictionary(x => x.Key, x => string.Join(",", x.Value));
        return new((int)response.StatusCode, timer.ElapsedMilliseconds, responseBody, responseHeaders);
    }

    private static async Task<string> ReadLimited(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + read > 1_000_000) throw new InvalidOperationException("Response exceeded the 1 MB limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }
}