using JustyBase.Services.Workspace;

namespace JustyBase.Tests;

public sealed class SqlSymbolIndexServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "JustyBase.Tests",
        "symbols",
        Guid.NewGuid().ToString("N"));

    public SqlSymbolIndexServiceTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void Extract_FindsCteTableAliasAndStatementStart()
    {
        const string sql = """
            WITH recent AS (
              SELECT id FROM sales
            )
            SELECT *
            FROM recent r
            JOIN customers c ON c.id = r.id;
            """;

        IReadOnlyList<SqlIndexedSymbol> symbols = SqlSymbolExtractor.Extract("a.sql", sql);

        Assert.Contains(symbols, s => s.Name == "WITH" && s.Kind == SqlIndexedSymbolKind.Statement);
        Assert.Contains(symbols, s => s.Name == "recent" && s.Kind == SqlIndexedSymbolKind.Cte);
        Assert.Contains(symbols, s => s.Name == "sales" && s.Kind == SqlIndexedSymbolKind.Table);
        Assert.Contains(symbols, s => s.Name == "customers" && s.Kind == SqlIndexedSymbolKind.Table);
        Assert.Contains(symbols, s => s.Name == "c" && s.Kind == SqlIndexedSymbolKind.Alias);
        Assert.All(symbols, s => Assert.Equal("a.sql", s.FilePath));
    }

    [Fact]
    public void Extract_ReportsOneBasedLineAndColumn()
    {
        const string sql = "SELECT 1;\n  SELECT id FROM sales;\n";

        IReadOnlyList<SqlIndexedSymbol> symbols = SqlSymbolExtractor.Extract("a.sql", sql);
        SqlIndexedSymbol sales = Assert.Single(symbols, s => s.Name == "sales");

        Assert.Equal(2, sales.Line);
        Assert.Equal(18, sales.Column);
    }

    [Fact]
    public void Extract_FindsQualifiedTableAfterSchema()
    {
        IReadOnlyList<SqlIndexedSymbol> symbols = SqlSymbolExtractor.Extract(
            "a.sql",
            "SELECT * FROM JUST_DATA.ADMIN.DIMDATE;");

        Assert.Contains(symbols, s => s.Name == "DIMDATE" && s.Kind == SqlIndexedSymbolKind.Table);
    }

    [Fact]
    public async Task Rebuild_IndexesSqlFilesRecursively()
    {
        string nested = Path.Combine(_folder, "nested");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(
            Path.Combine(_folder, "one.sql"),
            "SELECT * FROM customers;");
        await File.WriteAllTextAsync(
            Path.Combine(nested, "two.sql"),
            "UPDATE customers SET name = 'x';");

        var index = new SqlSymbolIndexService();
        await index.RebuildAsync([_folder]);

        Assert.Equal(2, index.IndexedFileCount);
        Assert.Equal(2, index.FindReferences("customers").Count);
        Assert.Equal(2, index.Search("customer").Count);
    }

    [Fact]
    public async Task IndexDocument_ReplacesTextForOpenDocument()
    {
        var index = new SqlSymbolIndexService();
        string path = Path.Combine(_folder, "open.sql");
        await File.WriteAllTextAsync(path, "SELECT * FROM old_table;");
        await index.RebuildAsync([_folder]);

        index.IndexDocument(path, "SELECT * FROM new_table;");

        Assert.Empty(index.FindReferences("old_table"));
        Assert.Single(index.FindReferences("new_table"));
    }

    [Fact]
    public async Task Invalidate_DropsAFile()
    {
        var index = new SqlSymbolIndexService();
        string path = Path.Combine(_folder, "drop.sql");
        await File.WriteAllTextAsync(path, "SELECT * FROM customers;");
        await index.RebuildAsync([_folder]);

        index.Invalidate(path);

        Assert.Empty(index.FindReferences("customers"));
        Assert.Equal(0, index.IndexedFileCount);
    }
}
