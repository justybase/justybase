using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.PluginCommon.Enums;

namespace JustyBase.Services;

/// <summary>
/// Maps a connected database type to the SQL dialect used by the shared editor
/// intelligence stack (lexer, parser, linter, authoring catalog, outline).
/// Db2, Oracle, PostgreSQL, MySQL, Access and SQLite documents use their native
/// dialects from JustyBase.NetezzaSql. DuckDB has no dedicated dialect yet, so it
/// uses the PostgreSQL-compatible surface as the closest approximation (DuckDB's
/// grammar is PostgreSQL-derived and PostgreSQL's lint registry is empty, which
/// avoids Netezza-only diagnostics). Excel/File SQL keeps the historical
/// SQLite-compatible authoring surface while executing against the embedded
/// DuckDB materialization.
/// </summary>
public static class SqlDialectResolver
{
    public static SqlDialect ForDatabaseType(DatabaseTypeEnum databaseType) => databaseType switch
    {
        DatabaseTypeEnum.DB2 => SqlDialect.Db2,
        DatabaseTypeEnum.Oracle => SqlDialect.Oracle,
        DatabaseTypeEnum.PostgreSql => SqlDialect.PostgreSql,
        DatabaseTypeEnum.MySql => SqlDialect.MySql,
        DatabaseTypeEnum.MsSqlTrusted => SqlDialect.Mssql,
        DatabaseTypeEnum.Sqlite => SqlDialect.Sqlite,
        DatabaseTypeEnum.Access => SqlDialect.Access,
        DatabaseTypeEnum.Excel => SqlDialect.Sqlite,
        // DuckDB intentionally uses the PostgreSQL surface until a dedicated
        // SqlDialect.DuckDb exists (P4). Keeping Netezza here would suggest
        // Netezza-only syntax (GROOM, DISTRIBUTE ON) and run Netezza lint rules.
        DatabaseTypeEnum.DuckDB => SqlDialect.PostgreSql,
        _ => SqlDialect.Netezza,
    };

    /// <summary>
    /// Human-readable diagnostic source used by <c>SqlRiskAnalysisService</c>.
    /// Mirrors <see cref="DialectRuntime.DiagnosticSource"/> for native dialects
    /// but keeps the real engine name for approximations (DuckDB, Excel).
    /// </summary>
    public static string DiagnosticSource(DatabaseTypeEnum databaseType) => databaseType switch
    {
        DatabaseTypeEnum.DuckDB => "DuckDB SQL",
        DatabaseTypeEnum.Excel => "Excel SQL",
        DatabaseTypeEnum.NotSupportedDatabase => "Netezza SQL",
        _ => DialectRuntime.DiagnosticSource(ForDatabaseType(databaseType)),
    };
}
