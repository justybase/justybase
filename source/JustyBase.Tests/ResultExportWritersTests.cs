using System.Text.Json;
using System.Data;
using System.Text;
using JustyBase.Common.Tools;
using JustyBase.PluginCommon.Enums;

namespace JustyBase.Tests;

public sealed class ResultExportWritersTests
{
    private static DataTableReader CreateReader()
    {
        // The reader owns the table for the test duration; DataTableReader disposes it.
#pragma warning disable CA2000
        var table = new DataTable();
#pragma warning restore CA2000
        table.Columns.Add("ID", typeof(int));
        table.Columns.Add("NAME", typeof(string));
        table.Columns.Add("ACTIVE", typeof(bool));
        table.Rows.Add(1, "O'Hara", true);
        table.Rows.Add(2, DBNull.Value, false);
        return table.CreateDataReader();
    }

    [Fact]
    public async Task SqlScriptIncludesDdlInsertsNullsAndLiterals()
    {
        string path = Path.Combine(Path.GetTempPath(), $"jb_export_{Guid.NewGuid():N}.sql");
        try
        {
            using var reader = CreateReader();
            long rows = await ResultExportWriters.WriteSqlScriptAsync(
                reader,
                path,
                "\"APP\".\"T\"",
                DatabaseTypeEnum.DB2,
                ddl: "CREATE TABLE \"APP\".\"T\" (\"ID\" INTEGER);",
                encoding: new UTF8Encoding(false));

            string text = await File.ReadAllTextAsync(path);
            Assert.Equal(2, rows);
            Assert.Contains("CREATE TABLE", text);
            Assert.Contains("INSERT INTO \"APP\".\"T\" (\"ID\", \"NAME\", \"ACTIVE\") VALUES (1, 'O''Hara', 1);", text);
            Assert.Contains("VALUES (2, NULL, 0);", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task JsonExportWritesObjectsWithColumns()
    {
        string path = Path.Combine(Path.GetTempPath(), $"jb_export_{Guid.NewGuid():N}.json");
        try
        {
            using var reader = CreateReader();
            await ResultExportWriters.WriteJsonAsync(reader, path, Encoding.UTF8);

            string text = await File.ReadAllTextAsync(path);
            Assert.Contains("\"ID\": 1", text);
            using var document = JsonDocument.Parse(text);
            var first = document.RootElement[0];
            Assert.Equal(1, first.GetProperty("ID").GetInt32());
            Assert.Equal("O'Hara", first.GetProperty("NAME").GetString());
            Assert.True(first.GetProperty("ACTIVE").GetBoolean());
            Assert.Equal(JsonValueKind.Null, document.RootElement[1].GetProperty("NAME").ValueKind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task XmlExportWritesRowsAndColumnNames()
    {
        string path = Path.Combine(Path.GetTempPath(), $"jb_export_{Guid.NewGuid():N}.xml");
        try
        {
            using var reader = CreateReader();
            await ResultExportWriters.WriteXmlAsync(reader, path, Encoding.UTF8);

            string text = await File.ReadAllTextAsync(path);
            Assert.Contains("<results>", text);
            Assert.Contains("<row>", text);
            Assert.Contains("name=\"NAME\"", text);
            Assert.Contains("O'Hara", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TsvExportUsesTabsAndHeaders()
    {
        string path = Path.Combine(Path.GetTempPath(), $"jb_export_{Guid.NewGuid():N}.tsv");
        try
        {
            using var reader = CreateReader();
            await ResultExportWriters.WriteTsvAsync(reader, path, Encoding.UTF8);

            string[] lines = await File.ReadAllLinesAsync(path);
            Assert.StartsWith("ID\tNAME\tACTIVE", lines[0]);
            Assert.StartsWith("1\tO'Hara\tTrue", lines[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("O'Hara", DatabaseTypeEnum.DB2, "'O''Hara'")]
    [InlineData(true, DatabaseTypeEnum.DB2, "1")]
    [InlineData(true, DatabaseTypeEnum.PostgreSql, "TRUE")]
    [InlineData(false, DatabaseTypeEnum.Sqlite, "0")]
    public void LiteralsFollowTargetSyntax(object value, DatabaseTypeEnum type, string expected)
    {
        Assert.Equal(expected, ResultExportWriters.FormatLiteral(value, type));
    }

    [Fact]
    public void BinaryLiteralUsesTheTargetHexSyntax()
    {
        byte[] bytes = [0x4A, 0x42];

        Assert.Equal("BX'4A42'", ResultExportWriters.FormatLiteral(bytes, DatabaseTypeEnum.DB2));
        Assert.Equal("X'4A42'", ResultExportWriters.FormatLiteral(bytes, DatabaseTypeEnum.Sqlite));
        Assert.Equal("decode('4A42', 'hex')", ResultExportWriters.FormatLiteral(bytes, DatabaseTypeEnum.PostgreSql));
        Assert.Equal("HEXTORAW('4A42')", ResultExportWriters.FormatLiteral(bytes, DatabaseTypeEnum.Oracle));
    }
}
