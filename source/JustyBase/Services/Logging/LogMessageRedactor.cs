using System.Text.RegularExpressions;

namespace JustyBase.Services.Logging;

/// <summary>
/// Redacts passwords and connection-string values from log text before persistence.
/// </summary>
public static partial class LogMessageRedactor
{
    private const string Redacted = "***";

    [GeneratedRegex(
        @"\b(Password|Pwd|Pass)\s*(=)\s*(""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|[^;\s,&]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PasswordAssignmentRegex();

    [GeneratedRegex(
        @"(""(?:Password|Pwd|Pass)""\s*:\s*|'(?:Password|Pwd|Pass)'\s*:\s*)(""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|[^,\s}\]]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonPasswordRegex();

    [GeneratedRegex(
        @"\b(ConnectionString|Connection\s+String)\s*(=)\s*(""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|[^\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionStringAssignmentRegex();

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var result = ConnectionStringAssignmentRegex().Replace(text, $"$1$2{Redacted}");
        result = PasswordAssignmentRegex().Replace(result, $"$1$2{Redacted}");
        result = JsonPasswordRegex().Replace(result, $"$1\"{Redacted}\"");
        return result;
    }
}
