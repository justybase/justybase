using JustyBase.PluginCommon.Enums;
using JustyBase.Services.DataCopy;

namespace JustyBase.Tests;

public sealed class DataCopySqlBuilderTests
{
    private static string Quote(string name) => $"\"{name}\"";

    [Fact]
    public void CreateTableUsesPortableTypesAndNullability()
    {
        var columns = new[]
        {
            new DataCopyColumn("ID", "ID", "INTEGER", "INTEGER", Nullable: false),
            new DataCopyColumn("NAME", "NAME", "VARCHAR(64)", "VARCHAR(64)", Nullable: true),
        };

        string sql = DataCopySqlBuilder.BuildCreateTable("APP", "COPY", columns, DatabaseTypeEnum.DB2, Quote);

        Assert.Equal("CREATE TABLE \"APP\".\"COPY\" (\"ID\" INTEGER NOT NULL, \"NAME\" VARCHAR(64));", sql);
    }

    [Fact]
    public void InsertUsesTargetColumnNamesAndBoundParameters()
    {
        var columns = new[]
        {
            new DataCopyColumn("SOURCE_ID", "ID", "INTEGER", "INTEGER", Nullable: false),
            new DataCopyColumn("SOURCE_NAME", "NAME", "VARCHAR(64)", "VARCHAR(64)", Nullable: true),
        };

        string sql = DataCopySqlBuilder.BuildInsert("APP", "COPY", columns, DatabaseTypeEnum.PostgreSql, Quote);
        string db2Sql = DataCopySqlBuilder.BuildInsert("APP", "COPY", columns, DatabaseTypeEnum.DB2, Quote);

        Assert.Equal("INSERT INTO \"APP\".\"COPY\" (\"ID\", \"NAME\") VALUES (@p0, @p1)", sql);
        Assert.Equal("INSERT INTO \"APP\".\"COPY\" (\"ID\", \"NAME\") VALUES (?, ?)", db2Sql);
    }

    [Theory]
    [InlineData("INTEGER", DatabaseTypeEnum.DB2, "INTEGER")]
    [InlineData("BIGINT", DatabaseTypeEnum.PostgreSql, "BIGINT")]
    [InlineData("NUMERIC(18, 4)", DatabaseTypeEnum.DB2, "NUMERIC(18, 4)")]
    [InlineData("VARCHAR(255)", DatabaseTypeEnum.Sqlite, "VARCHAR(255)")]
    [InlineData("DOUBLE PRECISION", DatabaseTypeEnum.NetezzaSQL, "DOUBLE")]
    [InlineData("BOOLEAN", DatabaseTypeEnum.DB2, "SMALLINT")]
    [InlineData("BOOLEAN", DatabaseTypeEnum.PostgreSql, "BOOLEAN")]
    [InlineData("BYTEA", DatabaseTypeEnum.PostgreSql, "BYTEA")]
    [InlineData("TIMESTAMP", DatabaseTypeEnum.Sqlite, "TEXT")]
    [InlineData("CLOB", DatabaseTypeEnum.DB2, "CLOB")]
    [InlineData("CLOB", DatabaseTypeEnum.PostgreSql, "TEXT")]
    [InlineData("SOMETHING_NEW", DatabaseTypeEnum.DB2, "VARCHAR(4000)")]
    [InlineData("SOMETHING_NEW", DatabaseTypeEnum.Sqlite, "TEXT")]
    public void TypeMappingIsPortable(string sourceType, DatabaseTypeEnum targetType, string expected)
    {
        Assert.Equal(expected, DataCopySqlBuilder.MapType(sourceType, targetType));
    }

    [Fact]
    public void QualifyOmitsAnEmptySchema()
    {
        Assert.Equal("\"COPY\"", DataCopySqlBuilder.Qualify(Quote, "", "COPY"));
        Assert.Equal("\"APP\".\"COPY\"", DataCopySqlBuilder.Qualify(Quote, "APP", "COPY"));
    }
}
