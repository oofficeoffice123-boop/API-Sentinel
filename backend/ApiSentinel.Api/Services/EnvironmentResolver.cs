using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace ApiSentinel.Api.Services;

public sealed record EnvironmentValue(string Value, bool IsSecret);

public sealed partial class EnvironmentResolver(SentinelDb db, CredentialProtector protector)
{
    public async Task<IReadOnlyDictionary<string, EnvironmentValue>> GetActiveAsync(int userId, CancellationToken cancellationToken)
    {
        var environment = await db.Environments.Include(e => e.Variables)
            .SingleOrDefaultAsync(e => e.UserId == userId && e.IsActive, cancellationToken);
        if (environment is null) return new Dictionary<string, EnvironmentValue>(StringComparer.Ordinal);
        return environment.Variables.ToDictionary(v => v.Key,
            v => new EnvironmentValue(v.IsSecret ? protector.Unprotect(v.EncryptedValue!) : v.Value ?? "", v.IsSecret), StringComparer.Ordinal);
    }

    public static string Resolve(string? value, IReadOnlyDictionary<string, EnvironmentValue> variables)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        return VariablePattern().Replace(value, match =>
        {
            var key = match.Groups[1].Value;
            return variables.TryGetValue(key, out var variable) ? variable.Value : throw new ArgumentException($"Environment variable '{key}' is not defined.");
        });
    }

    public static Dictionary<string, string> ResolveHeaders(Dictionary<string, string>? headers, IReadOnlyDictionary<string, EnvironmentValue> variables) =>
        (headers ?? []).ToDictionary(pair => pair.Key, pair => Resolve(pair.Value, variables), StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex("\\{\\{([A-Za-z_][A-Za-z0-9_]*)\\}\\}", RegexOptions.CultureInvariant)]
    private static partial Regex VariablePattern();
}