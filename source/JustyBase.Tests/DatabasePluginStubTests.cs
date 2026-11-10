using System.Reflection;
using AccessPlugin;
using DB2Plugin;
using ExcelPlugin;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Database;
using JustyBase.SqliteDriver;
using PostgresPlugin;
using DuckDbService = DuckDBPlugin.DuckDB;
using OracleService = OraclePlugin.Oracle;
using MySqlService = MySqlPlugin.MySql;

namespace JustyBase.Tests;

public sealed class DatabasePluginStubTests
{
    private static readonly DbConnectionOptions Options = new(Database: "sample");

    [Fact]
    public void ExternalTableCatalog_NeverThrows()
    {
        foreach (DatabaseService service in CreateServices())
        {
            Assert.Null(Invoke<string?>(service, "GetExternalTableSql", "db"));
        }
    }

    [Fact]
    public void SynonymCatalog_ReturnsNullForEnginesWithoutSynonyms()
    {
        DatabaseService[] withoutSynonyms =
        [
            new Postgres(Options),
            new DuckDbService(Options),
            new MySqlService(Options),
            new Excel(Options),
            new Access(Options),
            new Sqlite(Options),
        ];

        foreach (DatabaseService service in withoutSynonyms)
        {
            Assert.Null(Invoke<string?>(service, "GetSynonymSql", "db"));
        }
    }

    [Fact]
    public void SynonymCatalog_ReturnsSqlForOracleAndDb2()
    {
        string? oracleSql = Invoke<string?>(new OracleService(Options), "GetSynonymSql", "db");
        string? db2Sql = Invoke<string?>(new DB2DatabaseService(Options), "GetSynonymSql", "db");

        Assert.Contains("ALL_SYNONYMS", oracleSql, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(db2Sql);
    }

    [Fact]
    public void ProceduresCatalog_ReturnsNullForStubEngines()
    {
        Assert.Null(Invoke<string?>(new MySqlService(Options), "GetProceduresSql", "db", ""));
        Assert.Null(Invoke<string?>(new DuckDbService(Options), "GetProceduresSql", "db", ""));
    }

    [Fact]
    public async Task MySqlCreateTableTemplate_ReturnsACommentInsteadOfThrowing()
    {
        var service = new MySqlService(Options);

        string sql = await service.GetCreateTableText("db", "public", "sample");

        Assert.Contains("not implemented", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OracleHelpers_ProduceValidSql()
    {
        var service = new OracleService(Options);

        Assert.Contains("COMMENT ON TABLE", service.GetAddComment("t", "db", "s"), StringComparison.Ordinal);
        Assert.Contains("EXPLAIN PLAN FOR", service.GetExplainCode("db", "s", "t"), StringComparison.Ordinal);
        Assert.Contains("CREATE SEQUENCE", service.GetCreateSequencePatternText(), StringComparison.Ordinal);
        Assert.DoesNotContain("AS BIGINT", service.GetCreateSequencePatternText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AccessAndDuckDbEmptyTable_UseDeleteFrom()
    {
        Assert.Contains("DELETE FROM", new Access(Options).GetEmpty("t", "db", "s"), StringComparison.Ordinal);
        Assert.Contains("DELETE FROM", new DuckDbService(Options).GetEmpty("t", "db", "s"), StringComparison.Ordinal);
    }

    [Fact]
    public void DropView_UsesDropViewStatement()
    {
        Assert.Contains("DROP VIEW", new Postgres(Options).GetDropView("v", "db", "public"), StringComparison.Ordinal);
        Assert.Contains("DROP VIEW", new DuckDbService(Options).GetDropView("v", "db", "main"), StringComparison.Ordinal);
    }

    private static IEnumerable<DatabaseService> CreateServices()
    {
        yield return new Postgres(Options);
        yield return new DB2DatabaseService(Options);
        yield return new DuckDbService(Options);
        yield return new OracleService(Options);
        yield return new MySqlService(Options);
        yield return new Excel(Options);
        yield return new Access(Options);
        yield return new Sqlite(Options);
    }

    private static T? Invoke<T>(object target, string methodName, params object[] arguments)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        MethodInfo? method = null;
        for (Type? type = target.GetType(); type is not null && method is null; type = type.BaseType)
        {
            method = type.GetMethod(methodName, Flags | BindingFlags.DeclaredOnly);
        }

        if (method is null)
        {
            throw new InvalidOperationException($"{target.GetType().Name}.{methodName} not found.");
        }

        return (T?)method.Invoke(target, arguments);
    }
}
