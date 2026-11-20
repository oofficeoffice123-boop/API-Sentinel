using System.Text.Json;
using System.Text.Json.Nodes;

namespace ApiSentinel.Api.Services;

public static class OpenApiExampleBuilder
{
    public static string? Build(JsonElement schema) => BuildNode(schema, 0)?.ToJsonString();

    private static JsonNode? BuildNode(JsonElement schema, int depth)
    {
        if (depth > 12 || schema.ValueKind != JsonValueKind.Object) return null;
        if (schema.TryGetProperty("example", out var example)) return JsonNode.Parse(example.GetRawText());
        if (schema.TryGetProperty("enum", out var values) && values.ValueKind == JsonValueKind.Array && values.GetArrayLength() > 0)
            return JsonNode.Parse(values[0].GetRawText());
        if (!schema.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return null;

        return type.GetString() switch
        {
            "object" => BuildObject(schema, depth),
            "array" => BuildArray(schema, depth),
            "string" => JsonValue.Create(string.Empty),
            "integer" or "number" => JsonValue.Create(0),
            "boolean" => JsonValue.Create(false),
            "null" => null,
            _ => null
        };
    }

    private static JsonObject BuildObject(JsonElement schema, int depth)
    {
        var result = new JsonObject();
        if (!schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object) return result;
        foreach (var property in properties.EnumerateObject().Take(50))
        {
            var value = BuildNode(property.Value, depth + 1);
            if (value is not null) result[property.Name] = value;
        }
        return result;
    }

    private static JsonArray BuildArray(JsonElement schema, int depth)
    {
        var result = new JsonArray();
        if (schema.TryGetProperty("items", out var items))
        {
            var item = BuildNode(items, depth + 1);
            if (item is not null) result.Add(item);
        }
        return result;
    }
}