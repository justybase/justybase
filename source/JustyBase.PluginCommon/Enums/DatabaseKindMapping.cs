using JustyBase.ImportExport.Import;

namespace JustyBase.PluginCommon.Enums;

/// <summary>Maps the host <see cref="DatabaseTypeEnum"/> onto the neutral <see cref="DatabaseKind"/>.</summary>
public static class DatabaseKindMapping
{
    public static DatabaseKind ToDatabaseKind(this DatabaseTypeEnum databaseType) => databaseType switch
    {
        DatabaseTypeEnum.NetezzaSQL => DatabaseKind.Netezza,
        DatabaseTypeEnum.DB2 => DatabaseKind.Db2,
        DatabaseTypeEnum.MsSqlTrusted => DatabaseKind.MsSql,
        DatabaseTypeEnum.Oracle => DatabaseKind.Oracle,
        DatabaseTypeEnum.Sqlite => DatabaseKind.Sqlite,
        DatabaseTypeEnum.PostgreSql => DatabaseKind.PostgreSql,
        DatabaseTypeEnum.DuckDB => DatabaseKind.DuckDb,
        DatabaseTypeEnum.MySql => DatabaseKind.MySql,
        _ => DatabaseKind.Netezza
    };

    public static DatabaseTypeEnum ToDatabaseTypeEnum(this DatabaseKind databaseKind) => databaseKind switch
    {
        DatabaseKind.Netezza => DatabaseTypeEnum.NetezzaSQL,
        DatabaseKind.Db2 => DatabaseTypeEnum.DB2,
        DatabaseKind.MsSql => DatabaseTypeEnum.MsSqlTrusted,
        DatabaseKind.Oracle => DatabaseTypeEnum.Oracle,
        DatabaseKind.Sqlite => DatabaseTypeEnum.Sqlite,
        DatabaseKind.PostgreSql => DatabaseTypeEnum.PostgreSql,
        DatabaseKind.DuckDb => DatabaseTypeEnum.DuckDB,
        DatabaseKind.MySql => DatabaseTypeEnum.MySql,
        _ => DatabaseTypeEnum.NetezzaSQL
    };
}
