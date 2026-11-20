using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ApiSentinel.Api.Services;

public sealed class GroqAiService(IConfiguration configuration, IHttpClientFactory clients, ILogger<GroqAiService> logger)
{
    public async Task<JsonElement> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var key = configuration["GROQ_API_KEY"];
        if (string.IsNullOrWhiteSpace(key)) throw new AiUnavailableException();
        var client = clients.CreateClient("groq");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var payload = JsonSerializer.Serialize(new
        {
            model = configuration["Groq:Model"] ?? "openai/gpt-oss-120b",
            messages = new[] { new { role = "system", content = systemPrompt }, new { role = "user", content = AiInputRedactor.RedactText(userPrompt) } },
            response_format = new { type = "json_object" }
        });
        HttpResponseMessage response;
        try { response = await client.PostAsync("chat/completions", new StringContent(payload, Encoding.UTF8, "application/json"), cancellationToken); }
        catch (HttpRequestException) { throw new AiUnavailableException(); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new AiUnavailableException(); }
        using (response)
        {
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Groq returned HTTP {StatusCode}", (int)response.StatusCode);
            throw new AiUnavailableException();
        }
        try
        {
            using var envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var content = envelope.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            using var result = JsonDocument.Parse(content ?? "");
            return result.RootElement.Clone();
        }
        catch (JsonException) { throw new AiInvalidResponseException(); }
        }
    }
}

public sealed class AiUnavailableException : Exception { }
public sealed class AiInvalidResponseException : Exception { }