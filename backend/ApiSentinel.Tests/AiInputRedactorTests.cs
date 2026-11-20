using ApiSentinel.Api.Services;

namespace ApiSentinel.Tests;

public sealed class AiInputRedactorTests
{
    [Fact]
    public void Redacts_sensitive_fields_nested_tokens_and_personal_identifiers()
    {
        const string body = """{"access_token":"response-token-123","sessionId":"session-value-123","Cookie":"cookie-value-123","profile":{"email":"person@example.test","display":"safe"},"message":"Authorization: Bearer abc.def.ghi"}""";

        var redacted = AiInputRedactor.RedactResponseBody(body);

        Assert.DoesNotContain("response-token-123", redacted);
        Assert.DoesNotContain("session-value-123", redacted);
        Assert.DoesNotContain("cookie-value-123", redacted);
        Assert.DoesNotContain("person@example.test", redacted);
        Assert.DoesNotContain("abc.def.ghi", redacted);
        Assert.Contains("safe", redacted);
    }

    [Fact]
    public void Redacts_credentials_in_prompts_and_non_json_responses()
    {
        var exampleCredential = string.Concat("gsk", "_testsecret0123456789");
        Assert.DoesNotContain(exampleCredential, AiInputRedactor.RedactText("api_key=" + exampleCredential));
        Assert.DoesNotContain("pw-for-test", AiInputRedactor.RedactResponseBody("password: pw-for-test"));
        Assert.Contains("plain response", AiInputRedactor.RedactResponseBody("plain response"));
    }
}