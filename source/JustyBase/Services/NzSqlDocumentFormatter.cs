using System.Text;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Formatter;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;

namespace JustyBase.Services;

/// <summary>Formats SQL documents using the shared AST formatter (no third-party formatters).</summary>
public static class NzSqlDocumentFormatter
{
    /// <summary>
    /// Indent unit emitted by <c>NzSqlFormatter</c> (sibling project, <c>IndentSize = 2</c>).
    /// <see cref="RescaleIndent"/> divides by this before applying a custom width,
    /// so it must be kept in sync with the shared formatter.
    /// </summary>
    private const int FormatterBaseIndent = 2;

    private static readonly SqlFormatOptions DefaultOptions = new();

    public static string Format(string sql) => Format(sql, SqlDialect.Netezza);

    /// <summary>
    /// Formats <paramref name="sql"/> with the tokenizer/parser of the given dialect
    /// (Db2 documents get the Db2 dialect from JustyBase.NetezzaSql).
    /// </summary>
    public static string Format(string sql, SqlDialect dialect)
        => Format(sql, dialect, DefaultOptions);

    /// <summary>Formats with host-side options (keyword case, indent width).</summary>
    public static string Format(string sql, SqlDialect dialect, SqlFormatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var formatted = FormatCore(sql, dialect);
        if (options.KeywordCase != SqlKeywordCase.Preserve)
        {
            formatted = ApplyKeywordCase(formatted, dialect, options.KeywordCase);
        }

        if (options.IndentWidth is >= 0 and not 2)
        {
            formatted = RescaleIndent(formatted, options.IndentWidth);
        }

        return formatted;
    }

    private static string FormatCore(string sql, SqlDialect dialect)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return sql;

        try
        {
            var tokens = DialectRuntime.Tokenize(sql, dialect).ToArray();
            if (tokens.Length == 0)
                return sql;

            var formatter = new NzSqlFormatter();
            var output = new StringBuilder();
            var index = 0;
            var formattedAny = false;

            while (index < tokens.Length)
            {
                while (index < tokens.Length && tokens[index].Kind == NzToken.Semicolon)
                    index++;

                if (index >= tokens.Length)
                    break;

                var remaining = tokens.Skip(index).ToArray();
                var parser = DialectRuntime.CreateParser(remaining, dialect);
                var statement = parser.Parse();
                if (statement is null || parser.Position <= 0)
                    break;

                if (formattedAny)
                    output.AppendLine().AppendLine();

                output.Append(formatter.FormatStatement(statement));
                formattedAny = true;
                index += parser.Position;
            }

            return formattedAny ? output.ToString() : sql;
        }
        catch
        {
            return sql;
        }
    }

    private static string ApplyKeywordCase(string formatted, SqlDialect dialect, SqlKeywordCase keywordCase)
    {
        try
        {
            var tokens = DialectRuntime.Tokenize(formatted, dialect).ToArray();
            if (tokens.Length == 0)
            {
                return formatted;
            }

            var sb = new StringBuilder(formatted.Length);
            var cursor = 0;
            foreach (var token in tokens)
            {
                var text = token.ToStringValue();
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                var start = token.Position.Absolute;
                if (start < 0 || start < cursor || start + text.Length > formatted.Length)
                {
                    continue;
                }

                if (start > cursor)
                {
                    sb.Append(formatted, cursor, start - cursor);
                }

                sb.Append(ShouldPreserveCase(token.Kind) ? text : ApplyCase(text, keywordCase));
                cursor = start + text.Length;
            }

            if (cursor < formatted.Length)
            {
                sb.Append(formatted, cursor, formatted.Length - cursor);
            }

            return sb.Length == 0 ? formatted : sb.ToString();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return formatted;
        }
    }

    /// <summary>
    /// Keywords are cased; identifiers, literals, parameters, variables and
    /// comments keep their original spelling. Function names lex as identifiers,
    /// so they are preserved rather than following <c>KeywordCase</c>.
    /// </summary>
    /// <summary>
    /// Keywords are cased; identifiers, literals, parameters, variables and
    /// comments keep their original spelling. Function names lex as identifiers,
    /// so they are preserved rather than following <c>KeywordCase</c>.
    /// </summary>
    private static bool ShouldPreserveCase(NzToken kind) =>
        kind.IsIdentifierLike()
        || kind.IsParameterLike()
        || kind is NzToken.StringLiteral
            or NzToken.NumberLiteral
            or NzToken.QuotedIdentifier
            or NzToken.WhiteSpace
            or NzToken.LineComment
            or NzToken.BlockComment
            or NzToken.BracedVariable
            or NzToken.BracesOnlyVariable
            or NzToken.DollarNumber
            or NzToken.DollarIdentifier
            or NzToken.AmpersandIdentifier
            or NzToken.Unknown;

    private static string ApplyCase(string text, SqlKeywordCase keywordCase) => keywordCase switch
    {
        SqlKeywordCase.Lower => text.ToLowerInvariant(),
        SqlKeywordCase.Upper => text.ToUpperInvariant(),
        _ => text,
    };

    private static string RescaleIndent(string formatted, int indentWidth)
    {
        var lines = formatted.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
            {
                continue;
            }

            var leading = 0;
            while (leading < line.Length && line[leading] == ' ')
            {
                leading++;
            }

            if (leading == 0)
            {
                continue;
            }

            var level = leading / FormatterBaseIndent;
            if (level == 0)
            {
                continue;
            }

            lines[i] = new string(' ', level * indentWidth) + line[leading..];
        }

        return string.Join('\n', lines);
    }
}
