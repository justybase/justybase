using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;

namespace JustyBase.Services;

public sealed record SqlOutlineEntry(string Title, string Kind, int StartOffset, int Depth);

/// <summary>Builds a structural outline from parsed Netezza SQL (statements, CTEs).</summary>
public static class SqlOutlineBuilder
{
    public static IReadOnlyList<SqlOutlineEntry> Build(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return [];

        try
        {
            var tokens = NzLexer.Tokenize(sql).ToArray();
            if (tokens.Length == 0)
                return [];

            var entries = new List<SqlOutlineEntry>();
            var index = 0;
            var statementIndex = 0;

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

                var startOffset = tokens[index].Position.Absolute;
                statementIndex++;
                entries.Add(new SqlOutlineEntry($"{StatementLabel(statement)} #{statementIndex}", "Statement", startOffset, 0));
                AppendCteEntries(statement, entries, depth: 1);

                index += parser.Position;
            }

            return entries;
        }
        catch
        {
            return [];
        }
    }

    private static void AppendCteEntries(Statement statement, List<SqlOutlineEntry> entries, int depth)
    {
        if (statement is not SelectStatement select || select.With?.Ctes is null)
            return;

        foreach (var cte in select.With.Ctes)
        {
            var name = cte.Name;
            var offset = cte.Position.Absolute;
            entries.Add(new SqlOutlineEntry(name, "CTE", offset, depth));
        }
    }

    private static string StatementLabel(Statement statement) => statement switch
    {
        SelectStatement => "SELECT",
        InsertStatement => "INSERT",
        UpdateStatement => "UPDATE",
        DeleteStatement => "DELETE",
        MergeStatement => "MERGE",
        CreateTableStatement => "CREATE TABLE",
        CreateViewStatement => "CREATE VIEW",
        CreateProcedureStatement => "CREATE PROCEDURE",
        _ => statement.GetType().Name.Replace("Statement", "", StringComparison.Ordinal).ToUpperInvariant()
    };
}
