using Microsoft.OpenApi.Readers;
using Microsoft.OpenApi.Writers;

namespace ApiSentinel.Api.Services;

public static class OpenApiJsonNormalizer
{
    public static bool TryNormalizeYaml(string specification, out string json)
    {
        json = specification;
        if (specification.AsSpan().TrimStart().StartsWith("{")) return true;
        try
        {
            var reader = new OpenApiStringReader();
            var document = reader.Read(specification, out var diagnostic);
            if (document is null || diagnostic.Errors.Count > 0) return false;
            using var text = new StringWriter();
            var writer = new OpenApiJsonWriter(text);
            document.SerializeAsV3(writer);
            writer.Flush();
            json = text.ToString();
            return true;
        }
        catch (Exception e) when (e is Microsoft.OpenApi.Exceptions.OpenApiException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}