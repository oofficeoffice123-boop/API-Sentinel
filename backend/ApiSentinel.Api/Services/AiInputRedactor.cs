using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ApiSentinel.Api.Services;

public static partial class AiInputRedactor
{
    private const string Redacted = "[REDACTED]";

    [GeneratedRegex("(?i)([\\\"']?(?:password|passwd|token|api[-_]?key|secret|authorization|auth|cookie|session|credential|signature|private[-_]?key|client[-_]?secret)[A-Za-z0-9_-]*[\\\"']?\\s*[:=]\\s*[\\\"'])((?:\\\\.|[^\\\"'\\\\])*)([\\\"'])", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedCredentialAssignment();

    [GeneratedRegex("(?i)([\\\"']?(?:password|passwd|token|api[-_]?key|secret|authorization|auth|cookie|session|credential|signature|private[-_]?key|client[-_]?secret)[A-Za-z0-9_-]*[\\\"']?\\s*[:=]\\s*)([^\\s,;&\\\"'}]+)", RegexOptions.CultureInvariant)]
    private static partial Regex CredentialAssignment();

    [GeneratedRegex("(?i)\\bBearer\\s+[A-Za-z0-9._~+/-]+=*", RegexOptions.CultureInvariant)]
    private static partial Regex BearerValue();

    [GeneratedRegex("\\bgsk_[A-Za-z0-9_-]{12,}\\b", RegexOptions.CultureInvariant)]
    private static partial Regex GroqKey();

    [GeneratedRegex("\\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailAddress();

    public static string RedactText(string value)
    {
        var text = BearerValue().Replace(value, "Bearer " + Redacted);
        text = QuotedCredentialAssignment().Replace(text, "$1" + Redacted + "$3");
        text = CredentialAssignment().Replace(text, "$1" + Redacted);
        text = GroqKey().Replace(text, Redacted);
        return EmailAddress().Replace(text, Redacted);
    }

    public static string RedactResponseBody(string body)
    {
        try
        {
            var json = JsonNode.Parse(body);
            if (json is null) return RedactText(body);
            if (json is JsonValue scalar && scalar.TryGetValue<string>(out var text)) return JsonSerializer.Serialize(RedactText(text));
            RedactNode(json, 0);
            return RedactText(json.ToJsonString());
        }
        catch (JsonException)
        {
            return RedactText(body);
        }
    }

    private static void RedactNode(JsonNode node, int depth)
    {
        if (depth > 24) return;
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (SensitiveValueClassifier.IsSensitiveName(property.Key)) obj[property.Key] = Redacted;
                else if (property.Value is JsonValue value && value.TryGetValue<string>(out var text)) obj[property.Key] = RedactText(text);
                else if (property.Value is not null) RedactNode(property.Value, depth + 1);
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is JsonValue value && value.TryGetValue<string>(out var text)) array[index] = RedactText(text);
                else if (array[index] is { } item) RedactNode(item, depth + 1);
            }
        }
    }
}