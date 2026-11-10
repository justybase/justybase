using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.PluginCommon.Enums;
using JustyBase.Services;

namespace JustyBase.Tests;

public sealed class SqlDialectResolverTests
{
    [Theory]
    [InlineData(DatabaseTypeEnum.DB2, SqlDialect.Db2)]
    [InlineData(DatabaseTypeEnum.NetezzaSQL, SqlDialect.Netezza)]
    [InlineData(DatabaseTypeEnum.Oracle, SqlDialect.Oracle)]
    [InlineData(DatabaseTypeEnum.PostgreSql, SqlDialect.PostgreSql)]
    [InlineData(DatabaseTypeEnum.MySql, SqlDialect.MySql)]
    [InlineData(DatabaseTypeEnum.Sqlite, SqlDialect.Sqlite)]
    [InlineData(DatabaseTypeEnum.DuckDB, SqlDialect.PostgreSql)]
    [InlineData(DatabaseTypeEnum.Excel, SqlDialect.Sqlite)]
    [InlineData(DatabaseTypeEnum.Access, SqlDialect.Access)]
    [InlineData(DatabaseTypeEnum.MsSqlTrusted, SqlDialect.Mssql)]
    [InlineData(DatabaseTypeEnum.NotSupportedDatabase, SqlDialect.Netezza)]
    public void ForDatabaseType_MapsNativeDialectsAndDefaultsOthers(DatabaseTypeEnum databaseType, SqlDialect expected)
    {
        Assert.Equal(expected, SqlDialectResolver.ForDatabaseType(databaseType));
    }

    [Theory]
    [InlineData(DatabaseTypeEnum.DuckDB, "DuckDB SQL")]
    [InlineData(DatabaseTypeEnum.Excel, "Excel SQL")]
    [InlineData(DatabaseTypeEnum.Oracle, "Oracle SQL")]
    [InlineData(DatabaseTypeEnum.PostgreSql, "PostgreSQL SQL")]
    [InlineData(DatabaseTypeEnum.MySql, "MySQL SQL")]
    [InlineData(DatabaseTypeEnum.Sqlite, "SQLite SQL")]
    [InlineData(DatabaseTypeEnum.Access, "Access SQL")]
    [InlineData(DatabaseTypeEnum.DB2, "Db2 SQL")]
    [InlineData(DatabaseTypeEnum.MsSqlTrusted, "MSSQL SQL")]
    [InlineData(DatabaseTypeEnum.NetezzaSQL, "Netezza SQL")]
    [InlineData(DatabaseTypeEnum.NotSupportedDatabase, "Netezza SQL")]
    public void DiagnosticSource_UsesEngineNameForApproximations(DatabaseTypeEnum databaseType, string expected)
    {
        Assert.Equal(expected, SqlDialectResolver.DiagnosticSource(databaseType));
    }

    [Fact]
    public void ForDatabaseType_CoversEveryEnumValue()
    {
        foreach (DatabaseTypeEnum value in Enum.GetValues<DatabaseTypeEnum>())
        {
            SqlDialect dialect = SqlDialectResolver.ForDatabaseType(value);
            Assert.True(Enum.IsDefined(dialect), $"{value} mapped to an undefined dialect.");
        }
    }
}
