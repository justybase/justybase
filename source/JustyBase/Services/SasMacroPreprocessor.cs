using JustyBase.Core.Scripting;

namespace JustyBase.Services;

/// <summary>
/// SAS-like macro preprocessor. Canonical processing is
/// <see cref="AvaloniaScriptDialect"/> (%let / &amp;vars).
/// </summary>
public static class SasMacroPreprocessor
{
    private sealed record AmpersandDeclaration(int Start, int Length, string Name, string Value);

    private static readonly AvaloniaScriptDialect Dialect = new();
    private static readonly Dictionary<string, string> SessionMacros = new(StringComparer.OrdinalIgnoreCase);

    public static string Expand(string sql, IReadOnlyDictionary<string, string>? extraMacros = null)
    {
        if (string.IsNullOrEmpty(sql))
            return sql;

        Dictionary<string, string> merged;
        lock (SessionMacros)
        {
            merged = new Dictionary<string, string>(SessionMacros, StringComparer.OrdinalIgnoreCase);
        }
        if (extraMacros is not null)
        {
            foreach (var pair in extraMacros)
                merged[pair.Key.TrimStart('&', '%')] = pair.Value;
        }

        var result = Dialect.Process(new ScriptPreprocessRequest(sql, merged));
        lock (SessionMacros)
        {
            foreach (var pair in result.Variables)
                SessionMacros[pair.Key] = pair.Value;
        }

        return result.ProcessedSql;
    }

    public static void ClearSessionMacros()
    {
        lock (SessionMacros)
        {
            SessionMacros.Clear();
        }
    }

    /// <summary>
    /// Removes legacy <c>DECLARE &amp;name = value</c> directives while preserving
    /// every character offset and newline. The parser can then lint the remaining
    /// SQL without treating a session-variable directive as ordinary SQL.
    /// Declarations inside strings and comments are deliberately ignored.
    /// </summary>
    public static string RemoveAmpersandDeclarationsForAuthoring(string sql)
    {
        if (string.IsNullOrEmpty(sql))
            return sql;

        var chars = sql.ToCharArray();
        foreach (AmpersandDeclaration declaration in FindAmpersandDeclarations(sql))
        {
            int end = Math.Min(sql.Length, declaration.Start + declaration.Length);
            for (int i = declaration.Start; i < end; i++)
            {
                if (chars[i] is not '\r' and not '\n')
                    chars[i] = ' ';
            }
        }

        return new string(chars);
    }

    private static IReadOnlyList<AmpersandDeclaration> FindAmpersandDeclarations(string sql)
    {
        var declarations = new List<AmpersandDeclaration>();
        int index = 0;

        while (index < sql.Length)
        {
            if (sql[index] is '\'' or '"')
            {
                index = SkipQuoted(sql, index, sql[index]);
                continue;
            }

            if (index + 1 < sql.Length && sql[index] == '-' && sql[index + 1] == '-')
            {
                index = SkipLineComment(sql, index + 2);
                continue;
            }

            if (index + 1 < sql.Length && sql[index] == '/' && sql[index + 1] == '*')
            {
                index = SkipBlockComment(sql, index + 2);
                continue;
            }

            if (HasStatementBoundaryBefore(sql, index)
                && TryReadAmpersandDeclaration(sql, index, out AmpersandDeclaration? declaration))
            {
                declarations.Add(declaration!);
                index += declaration!.Length;
                continue;
            }

            index++;
        }

        return declarations;
    }

    private static bool TryReadAmpersandDeclaration(
        string sql,
        int start,
        out AmpersandDeclaration? declaration)
    {
        declaration = null;
        const string keyword = "declare";
        if (start + keyword.Length > sql.Length
            || !sql.AsSpan(start, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int index = start + keyword.Length;
        if (index < sql.Length && (char.IsLetterOrDigit(sql[index]) || sql[index] == '_'))
            return false;

        while (index < sql.Length && char.IsWhiteSpace(sql[index])) index++;
        if (index >= sql.Length || sql[index++] != '&') return false;
        if (index >= sql.Length || !IsIdentifierStart(sql[index])) return false;

        int nameStart = index++;
        while (index < sql.Length && IsIdentifierPart(sql[index])) index++;
        string name = sql[nameStart..index];

        while (index < sql.Length && char.IsWhiteSpace(sql[index])) index++;
        if (index >= sql.Length || sql[index++] != '=') return false;
        while (index < sql.Length && char.IsWhiteSpace(sql[index])) index++;

        int valueStart = index;
        bool singleQuoted = false;
        bool doubleQuoted = false;
        bool blockComment = false;
        while (index < sql.Length)
        {
            char current = sql[index];
            char next = index + 1 < sql.Length ? sql[index + 1] : '\0';

            if (blockComment)
            {
                if (current == '*' && next == '/')
                {
                    blockComment = false;
                    index += 2;
                }
                else
                {
                    index++;
                }
                continue;
            }

            if (singleQuoted)
            {
                if (current == '\'' && next == '\'') index += 2;
                else if (current == '\'') { singleQuoted = false; index++; }
                else index++;
                continue;
            }

            if (doubleQuoted)
            {
                if (current == '"' && next == '"') index += 2;
                else if (current == '"') { doubleQuoted = false; index++; }
                else index++;
                continue;
            }

            if (current == '\'') { singleQuoted = true; index++; continue; }
            if (current == '"') { doubleQuoted = true; index++; continue; }
            if (current == '/' && next == '*') { blockComment = true; index += 2; continue; }
            if (current == ';')
            {
                string value = sql[valueStart..index].Trim();
                declaration = new AmpersandDeclaration(start, index + 1 - start, name, value);
                return value.Length > 0;
            }

            if (current is '\r' or '\n')
                break;

            index++;
        }

        string lineValue = sql[valueStart..index].Trim();
        if (lineValue.Length == 0) return false;
        declaration = new AmpersandDeclaration(start, index - start, name, lineValue);
        return true;
    }

    private static bool HasStatementBoundaryBefore(string sql, int index)
    {
        int previous = index - 1;
        while (previous >= 0 && (sql[previous] is ' ' or '\t')) previous--;
        return previous < 0 || sql[previous] is '\r' or '\n' or ';';
    }

    private static int SkipQuoted(string sql, int index, char quote)
    {
        index++;
        while (index < sql.Length)
        {
            if (sql[index] == quote)
            {
                if (index + 1 < sql.Length && sql[index + 1] == quote) index += 2;
                else return index + 1;
            }
            else
            {
                index++;
            }
        }
        return index;
    }

    private static int SkipLineComment(string sql, int index)
    {
        while (index < sql.Length && sql[index] is not '\r' and not '\n') index++;
        return index;
    }

    private static int SkipBlockComment(string sql, int index)
    {
        while (index + 1 < sql.Length)
        {
            if (sql[index] == '*' && sql[index + 1] == '/') return index + 2;
            index++;
        }
        return sql.Length;
    }

    private static bool IsIdentifierStart(char value)
        => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';

    private static bool IsIdentifierPart(char value)
        => IsIdentifierStart(value) || value is >= '0' and <= '9';
}
