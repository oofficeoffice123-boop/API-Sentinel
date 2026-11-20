using System.Text.Json;
using ApiSentinel.Api;
using ApiSentinel.Api.Services;

namespace ApiSentinel.Tests;

public sealed class AssertionEvaluatorTests
{
    [Fact]
    public void Extracts_json_values_for_collection_variables()
    {
        Assert.Equal("access-token", AssertionEvaluator.ExtractJsonPath("{\"auth\":{\"token\":\"access-token\"}}", "$.auth.token"));
        Assert.Null(AssertionEvaluator.ExtractJsonPath("{\"auth\":{}}", "$.auth.token"));
    }

    [Theory]
    [InlineData("$.user.id", "Equals", "7", true)]
    [InlineData("$.items[0].name", "Contains", "\"api\"", true)]
    [InlineData("$.user.id", "GreaterThan", "5", true)]
    [InlineData("$.user.missing", "DoesNotExist", null, true)]
    [InlineData("$.user.id", "LessThan", "5", false)]
    public void Evaluates_json_path_assertions(string path, string op, string? expected, bool pass)
    {
        var assertion = new TestAssertion { JsonPath = path, Operator = op, ExpectedJson = expected };
        var result = AssertionEvaluator.Evaluate("{\"user\":{\"id\":7},\"items\":[{\"name\":\"api sentinel\"}]}", assertion);
        Assert.Equal(pass, result.Passed);
    }

    [Fact]
    public void Evaluates_response_header_assertions_case_insensitively()
    {
        var assertion = new TestAssertion { JsonPath = "header:content-type", Operator = "Contains", ExpectedJson = "\"application/json\"" };

        var result = AssertionEvaluator.Evaluate("{}", assertion, new Dictionary<string, string> { ["Content-Type"] = "application/json; charset=utf-8" });

        Assert.True(result.Passed);
        Assert.Equal("\"application/json; charset=utf-8\"", result.Actual);
    }

    [Fact]
    public void Missing_response_header_fails_exists_assertion()
    {
        var assertion = new TestAssertion { JsonPath = "header:X-Request-Id", Operator = "Exists" };

        var result = AssertionEvaluator.Evaluate("not json", assertion, new Dictionary<string, string>());

        Assert.False(result.Passed);
    }

    [Fact]
    public void Invalid_json_path_does_not_pass_does_not_exist()
    {
        var assertion = new TestAssertion { JsonPath = "$.user..id", Operator = "DoesNotExist" };

        var result = AssertionEvaluator.Evaluate("{\"user\":{}}", assertion);

        Assert.False(result.Passed);
        Assert.Equal("JSON path is invalid.", result.Error);
    }
}