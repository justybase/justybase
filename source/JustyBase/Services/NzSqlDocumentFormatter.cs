using System.Text;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Formatter;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;

namespace JustyBase.Services;

/// <summary>Formats SQL documents using the shared AST formatter (no third-party formatters).</summary>
public static class NzSqlDocumentFormatter
{
    public static string Format(string sql) => Format(sql, SqlDialect.Netezza);

    /// <summary>
    /// Formats <paramref name="sql"/> with the tokenizer/parser of the given dialect
    /// (Db2 documents get the Db2 dialect from JustyBase.NetezzaSql).
    /// </summary>
    public static string Format(string sql, SqlDialect dialect)
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
}
