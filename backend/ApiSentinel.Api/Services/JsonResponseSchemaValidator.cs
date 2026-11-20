using System.Text.Json;

namespace ApiSentinel.Api.Services;

public static class JsonResponseSchemaValidator
{
    private static readonly HashSet<string> SupportedKeywords = ["type", "properties", "required", "items", "enum"];

    public static bool IsValidDefinition(string schema)
    {
        try
        {
            using var document = JsonDocument.Parse(schema, new JsonDocumentOptions { MaxDepth = 24 });
            return ValidateDefinition(document.RootElement, 0);
        }
        catch (JsonException) { return false; }
    }

    public static string? Validate(string response, string schema)
    {
        try
        {
            using var actual = JsonDocument.Parse(response, new JsonDocumentOptions { MaxDepth = 64 });
            using var definition = JsonDocument.Parse(schema, new JsonDocumentOptions { MaxDepth = 24 });
            return ValidateValue(actual.RootElement, definition.RootElement, "$", 0);
        }
        catch (JsonException) { return "Response or stored schema is not valid JSON."; }
    }

    private static bool ValidateDefinition(JsonElement schema, int depth)
    {
        if (depth > 16 || schema.ValueKind != JsonValueKind.Object || schema.EnumerateObject().Any(p => !SupportedKeywords.Contains(p.Name))) return false;
        if (schema.TryGetProperty("type", out var type) && (type.ValueKind != JsonValueKind.String || !IsSupportedType(type.GetString()))) return false;
        if (schema.TryGetProperty("required", out var required) && (required.ValueKind != JsonValueKind.Array || required.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String))) return false;
        if (schema.TryGetProperty("enum", out var values) && values.ValueKind != JsonValueKind.Array) return false;
        if (schema.TryGetProperty("properties", out var properties))
        {
            if (properties.ValueKind != JsonValueKind.Object || properties.EnumerateObject().Any(x => !ValidateDefinition(x.Value, depth + 1))) return false;
        }
        return !schema.TryGetProperty("items", out var items) || ValidateDefinition(items, depth + 1);
    }

    private static string? ValidateValue(JsonElement value, JsonElement schema, string path, int depth)
    {
        if (depth > 24) return $"{path} exceeds the supported schema depth.";
        if (schema.TryGetProperty("type", out var type) && !MatchesType(value, type.GetString()!)) return $"{path} must be {type.GetString()}.";
        if (schema.TryGetProperty("enum", out var options) && !options.EnumerateArray().Any(x => JsonEquals(value, x))) return $"{path} is not an allowed value.";
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required))
                foreach (var name in required.EnumerateArray().Select(x => x.GetString()!))
                    if (!value.TryGetProperty(name, out _)) return $"{path}.{name} is required.";
            if (schema.TryGetProperty("properties", out var properties))
                foreach (var property in properties.EnumerateObject())
                    if (value.TryGetProperty(property.Name, out var propertyValue) && ValidateValue(propertyValue, property.Value, $"{path}.{property.Name}", depth + 1) is { } error) return error;
        }
        if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (ValidateValue(item, items, $"{path}[{index}]", depth + 1) is { } error) return error;
                index++;
            }
        }
        return null;
    }

    private static bool IsSupportedType(string? type) => type is "object" or "array" or "string" or "number" or "integer" or "boolean" or "null";

    private static bool MatchesType(JsonElement value, string type) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && decimal.Truncate(number) == number,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => false
    };

    private static bool JsonEquals(JsonElement left, JsonElement right) => left.ValueKind == right.ValueKind && left.GetRawText() == right.GetRawText();
}