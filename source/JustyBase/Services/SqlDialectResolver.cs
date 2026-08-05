using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.PluginCommon.Enums;

namespace JustyBase.Services;

/// <summary>
/// Maps a connected database type to the SQL dialect used by the shared editor
/// intelligence stack (lexer, parser, linter, authoring catalog).
/// Db2 documents use the Db2 dialect from JustyBase.NetezzaSql; everything else
/// currently falls back to the Netezza default so existing behavior is preserved.
/// </summary>
public static class SqlDialectResolver
{
    public static SqlDialect ForDatabaseType(DatabaseTypeEnum databaseType) => databaseType switch
    {
        DatabaseTypeEnum.DB2 => SqlDialect.Db2,
        _ => SqlDialect.Netezza,
    };
}
