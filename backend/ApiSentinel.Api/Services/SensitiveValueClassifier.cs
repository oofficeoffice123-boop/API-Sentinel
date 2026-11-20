using System.Text.RegularExpressions;

namespace ApiSentinel.Api.Services;

public static partial class SensitiveValueClassifier
{
    [GeneratedRegex("(?:^|[-_])auth(?:$|[-_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthSegment();

    [GeneratedRegex("(?:^|[-_])key(?:$|[-_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeySegment();

    public static bool IsSensitiveName(string name)
    {
        var compact = new string(name.Where(char.IsAsciiLetterOrDigit).ToArray()).ToLowerInvariant();
        return new[] { "password", "passwd", "token", "secret", "apikey", "authorization", "authentication", "credential", "session", "signature", "privatekey", "cookie" }.Any(compact.Contains) ||
            name.Equals("auth", StringComparison.OrdinalIgnoreCase) || AuthSegment().IsMatch(name) || KeySegment().IsMatch(name);
    }
}