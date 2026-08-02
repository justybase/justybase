using System.Text;
using JustyBase.NetezzaSqlParser.Formatter;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;

namespace JustyBase.Services;

/// <summary>Formats SQL documents using the Netezza AST formatter (no third-party formatters).</summary>
public static class NzSqlDocumentFormatter
{
    public static string Format(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return sql;

        try
        {
            var tokens = NzLexer.Tokenize(sql).ToArray();
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
                var parser = new NzSqlParser(remaining);
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
