using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ApiSentinel.Api.Services;

public sealed record AssertionResult(string JsonPath, string Operator, string? Actual, string? Expected, bool Passed, string? Error = null);

public static partial class AssertionEvaluator
{
    public static string? ExtractJsonPath(string responseBody, string jsonPath)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var value = Resolve(document.RootElement, jsonPath);
            return value is null ? null : value.Value.ValueKind == JsonValueKind.String ? value.Value.GetString() : value.Value.GetRawText();
        }
        catch (JsonException) { return null; }
        catch (ArgumentException) { return null; }
    }

    public static AssertionResult Evaluate(string responseBody, TestAssertion assertion, IReadOnlyDictionary<string, string>? responseHeaders = null)
    {
        try
        {
            var isHeader = assertion.JsonPath.StartsWith("header:", StringComparison.OrdinalIgnoreCase);
            JsonElement? actual;
            JsonDocument document = null!;
            if (isHeader)
            {
                var headerName = assertion.JsonPath[7..];
                var matchingHeader = string.IsNullOrWhiteSpace(headerName) || responseHeaders is null
                    ? default(KeyValuePair<string, string>?)
                    : responseHeaders.FirstOrDefault(pair => pair.Key.Equals(headerName, StringComparison.OrdinalIgnoreCase));
                if (matchingHeader is null || string.IsNullOrEmpty(matchingHeader.Value.Key)) actual = null;
                else
                {
                    document = JsonDocument.Parse(JsonSerializer.Serialize(matchingHeader.Value.Value));
                    actual = document.RootElement;
                }
                if (actual is null) document = JsonDocument.Parse("null");
            }
            else
            {
                document = JsonDocument.Parse(responseBody);
                actual = Resolve(document.RootElement, assertion.JsonPath);
            }
            using (document)
            {
            using var expectedDocument = assertion.ExpectedJson is null ? null : JsonDocument.Parse(assertion.ExpectedJson);
            var expected = expectedDocument?.RootElement;
            var passed = assertion.Operator switch
            {
                "Exists" => actual.HasValue,
                "DoesNotExist" => !actual.HasValue,
                "IsNull" => actual is { ValueKind: JsonValueKind.Null },
                "IsNotNull" => actual.HasValue && actual.Value.ValueKind != JsonValueKind.Null,
                "Equals" => actual.HasValue && expected.HasValue && Equal(actual.Value, expected.Value),
                "NotEquals" => actual.HasValue && expected.HasValue && !Equal(actual.Value, expected.Value),
                "Contains" => actual is { ValueKind: JsonValueKind.String } && expected is { ValueKind: JsonValueKind.String } && actual.Value.GetString()!.Contains(expected.Value.GetString()!, StringComparison.Ordinal),
                "DoesNotContain" => actual is { ValueKind: JsonValueKind.String } && expected is { ValueKind: JsonValueKind.String } && !actual.Value.GetString()!.Contains(expected.Value.GetString()!, StringComparison.Ordinal),
                "GreaterThan" => CompareNumeric(actual, expected, true),
                "LessThan" => CompareNumeric(actual, expected, false),
                _ => false
            };
            return new(assertion.JsonPath, assertion.Operator, actual?.GetRawText(), assertion.ExpectedJson, passed, passed ? null : "Assertion did not match the response.");
            }
        }
        catch (JsonException)
        {
            return new(assertion.JsonPath, assertion.Operator, null, assertion.ExpectedJson, false, "Response or expected value was not valid JSON.");
        }
        catch (ArgumentException)
        {
            return new(assertion.JsonPath, assertion.Operator, null, assertion.ExpectedJson, false, "JSON path is invalid.");
        }
    }

    private static JsonElement? Resolve(JsonElement root, string path)
    {
        if (path == "$") return root;
        var current = root;
        if (path.StartsWith('$') && !path.StartsWith("$.", StringComparison.Ordinal) && !path.StartsWith("$[", StringComparison.Ordinal) || path.StartsWith('.')) throw new ArgumentException("JSON path must start with a property or array selector.");
        var normalized = path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path.StartsWith("$[", StringComparison.Ordinal) ? path[1..] : path;
        if (normalized.Length == 0) throw new ArgumentException("JSON path is empty.");
        var cursor = 0;
        var matches = PathToken().Matches(normalized);
        foreach (Match match in matches)
        {
            if (match.Index != cursor) throw new ArgumentException("JSON path contains an invalid segment.");
            cursor += match.Length;
            if (match.Groups[1].Success)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(match.Groups[1].Value, out current)) return null;
            }
            else if (current.ValueKind != JsonValueKind.Array || !int.TryParse(match.Groups[2].Value, out var index) || index >= current.GetArrayLength()) return null;
            else current = current[index];
        }
        if (matches.Count == 0 || cursor != normalized.Length) throw new ArgumentException("JSON path contains an invalid segment.");
        return current;
    }

    private static bool Equal(JsonElement a, JsonElement b) => a.ValueKind == b.ValueKind && a.ValueKind switch
    {
        JsonValueKind.String => a.GetString() == b.GetString(),
        JsonValueKind.Number => a.GetDecimal() == b.GetDecimal(),
        JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => a.ToString() == b.ToString(),
        _ => a.GetRawText() == b.GetRawText()
    };

    private static bool CompareNumeric(JsonElement? actual, JsonElement? expected, bool greater) =>
        actual is { ValueKind: JsonValueKind.Number } a && expected is { ValueKind: JsonValueKind.Number } e &&
        a.TryGetDecimal(out var left) && e.TryGetDecimal(out var right) && (greater ? left > right : left < right);

    [GeneratedRegex(@"(?:^|\.)([^.\[\]]+)|\[(\d+)\]")]
    private static partial Regex PathToken();
}