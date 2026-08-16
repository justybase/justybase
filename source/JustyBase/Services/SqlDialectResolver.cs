using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.PluginCommon.Enums;

namespace JustyBase.Services;

/// <summary>
/// Maps a connected database type to the SQL dialect used by the shared editor
/// intelligence stack (lexer, parser, linter, authoring catalog).
/// Db2 and SQLite documents use their dialects from JustyBase.NetezzaSql; other
/// database types retain the Netezza default for compatibility.
/// </summary>
public static class SqlDialectResolver
{
    public static SqlDialect ForDatabaseType(DatabaseTypeEnum databaseType) => databaseType switch
    {
        DatabaseTypeEnum.DB2 => SqlDialect.Db2,
        DatabaseTypeEnum.Sqlite => SqlDialect.Sqlite,
        _ => SqlDialect.Netezza,
    };
}
