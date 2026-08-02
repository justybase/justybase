using JustyBase.Core.Scripting;

namespace JustyBase.Services;

/// <summary>
/// SAS-like macro preprocessor. Canonical processing is
/// <see cref="AvaloniaScriptDialect"/> (%let / &amp;vars).
/// </summary>
public static class SasMacroPreprocessor
{
    private static readonly AvaloniaScriptDialect Dialect = new();
    private static readonly Dictionary<string, string> SessionMacros = new(StringComparer.OrdinalIgnoreCase);

    public static string Expand(string sql, IReadOnlyDictionary<string, string>? extraMacros = null)
    {
        if (string.IsNullOrEmpty(sql))
            return sql;

        var merged = new Dictionary<string, string>(SessionMacros, StringComparer.OrdinalIgnoreCase);
        if (extraMacros is not null)
        {
            foreach (var pair in extraMacros)
                merged[pair.Key.TrimStart('&', '%')] = pair.Value;
        }

        var result = Dialect.Process(new ScriptPreprocessRequest(sql, merged));
        foreach (var pair in result.Variables)
            SessionMacros[pair.Key] = pair.Value;

        return result.ProcessedSql;
    }

    public static void ClearSessionMacros() => SessionMacros.Clear();
}
