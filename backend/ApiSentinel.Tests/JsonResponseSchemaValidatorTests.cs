using ApiSentinel.Api.Services;

namespace ApiSentinel.Tests;

public sealed class JsonResponseSchemaValidatorTests
{
    [Fact]
    public void Validates_required_nested_fields_and_array_items()
    {
        const string schema = """{"type":"object","required":["user"],"properties":{"user":{"type":"object","required":["id"],"properties":{"id":{"type":"integer"}}},"roles":{"type":"array","items":{"type":"string","enum":["reader","admin"]}}}}""";
        Assert.True(JsonResponseSchemaValidator.IsValidDefinition(schema));
        Assert.Null(JsonResponseSchemaValidator.Validate("{\"user\":{\"id\":7},\"roles\":[\"reader\"]}", schema));
        Assert.Contains("id", JsonResponseSchemaValidator.Validate("{\"user\":{\"id\":\"7\"},\"roles\":[\"reader\"]}", schema));
    }

    [Theory]
    [InlineData("{\"type\":\"file\"}")]
    [InlineData("{\"type\":\"object\",\"customScript\":\"run\"}")]
    public void Rejects_unsupported_schema_features(string schema) => Assert.False(JsonResponseSchemaValidator.IsValidDefinition(schema));
}