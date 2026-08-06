using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.PluginCommon.Enums;
using JustyBase.Services;

namespace JustyBase.Tests;

public sealed class SqlDialectResolverTests
{
    [Theory]
    [InlineData(DatabaseTypeEnum.DB2, SqlDialect.Db2)]
    [InlineData(DatabaseTypeEnum.NetezzaSQL, SqlDialect.Netezza)]
    [InlineData(DatabaseTypeEnum.Oracle, SqlDialect.Netezza)]
    [InlineData(DatabaseTypeEnum.PostgreSql, SqlDialect.Netezza)]
    [InlineData(DatabaseTypeEnum.MySql, SqlDialect.Netezza)]
    [InlineData(DatabaseTypeEnum.Sqlite, SqlDialect.Netezza)]
    [InlineData(DatabaseTypeEnum.DuckDB, SqlDialect.Netezza)]
    [InlineData(DatabaseTypeEnum.NotSupportedDatabase, SqlDialect.Netezza)]
    public void ForDatabaseType_MapsDb2AndDefaultsOthers(DatabaseTypeEnum databaseType, SqlDialect expected)
    {
        Assert.Equal(expected, SqlDialectResolver.ForDatabaseType(databaseType));
    }
}
