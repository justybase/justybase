using System.Text;

namespace JustyBase.PluginDatabaseBase.Database;

public abstract partial class DatabaseService
{
    /// <summary>
    /// Escapes a value for use inside a single-quoted SQL string literal by doubling embedded quotes.
    /// </summary>
    protected static string EscapeSqlLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    /// <summary>
    /// Ensures a generated DDL/DML statement ends with a single semicolon, trimming trailing whitespace.
    /// </summary>
    protected static string EnsureSqlStatement(string ddl)
    {
        string trimmed = ddl.TrimEnd();
        return trimmed.EndsWith(';') ? trimmed : trimmed + ";";
    }

    /// <summary>
    /// Appends each statement on its own line. Empty collections are ignored.
    /// </summary>
    protected static void AppendSqlStatements(StringBuilder sb, IReadOnlyList<string> statements)
    {
        foreach (string statement in statements)
        {
            sb.AppendLine(statement);
        }
    }
}
