using System.Net;
using System.Text;
using System.Text.Json;
using ApiSentinel.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiSentinel.Tests;

public sealed class GroqAiServiceTests
{
    [Fact]
    public async Task Complete_redacts_quoted_secrets_and_bearer_tokens_before_sending_prompt()
    {
        string? sentPrompt = null;
        var handler = new StubHandler(async request =>
        {
            using var envelope = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            sentPrompt = envelope.RootElement.GetProperty("messages")[1].GetProperty("content").GetString();
            return Success("{\"ok\":true}");
        });
        var service = Create(handler);

        var response = await service.CompleteAsync("system", "{\"Authorization\":\"Bearer bearerSecret phrase\",\"password\":\"my private phrase\",\"Cookie\":\"cookie-session-secret\",\"sessionId\":\"private-session-secret\",\"contact\":\"owner@example.test\",\"public\":\"visible\"}", CancellationToken.None);

        Assert.True(response.GetProperty("ok").GetBoolean());
        Assert.DoesNotContain("bearerSecret", sentPrompt);
        Assert.DoesNotContain("private phrase", sentPrompt);
        Assert.DoesNotContain("cookie-session-secret", sentPrompt);
        Assert.DoesNotContain("private-session-secret", sentPrompt);
        Assert.DoesNotContain("owner@example.test", sentPrompt);
        Assert.Contains("visible", sentPrompt);
    }

    [Fact]
    public async Task Complete_rejects_malformed_model_json()
    {
        var service = Create(new StubHandler(_ => Task.FromResult(Success("not JSON"))));
        await Assert.ThrowsAsync<AiInvalidResponseException>(() => service.CompleteAsync("system", "prompt", CancellationToken.None));
    }

    [Fact]
    public async Task Complete_treats_provider_rate_limits_as_unavailable()
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        var service = Create(handler);
        await Assert.ThrowsAsync<AiUnavailableException>(() => service.CompleteAsync("system", "prompt", CancellationToken.None));
    }

    private static GroqAiService Create(HttpMessageHandler handler)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GROQ_API_KEY"] = "test-key" }).Build();
        return new GroqAiService(config, new StubClientFactory(handler), NullLogger<GroqAiService>.Instance);
    }

    private static HttpResponseMessage Success(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }), Encoding.UTF8, "application/json")
    };

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.groq.com/openai/v1/") };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}