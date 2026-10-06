using JustyBase.Models;
using JustyBase.Services.DataGrid;

namespace JustyBase.Tests;

public sealed class DataGridClipboardServiceTests
{
    [Fact]
    public async Task BuildAllRowsTextAsync_WithRows_ReturnsTabDelimitedText()
    {
        var service = new DataGridClipboardService();
        var table = new TableOfSqlResults
        {
            Headers = ["C1", "C2"],
            Rows = [new TableRow { Fields = ["ab\tcd", "line1\r\nline2"] }],
            FilteredRows = new BulkObservableCollection<TableRow>
            {
                new TableRow { Fields = ["ab\tcd", "line1\r\nline2"] }
            },
        };

        string text = await service.BuildAllRowsTextAsync(table, ["C1", "C2"]);

        Assert.Contains("C1\tC2", text);
        Assert.Contains("ab cd\tline1 line2", text);
    }

    [Fact]
    public void BuildMultiRowText_WithSelectedRows_ReturnsHeadersAndRows()
    {
        var service = new DataGridClipboardService();
        var selectedItems = new List<object>
        {
            new TableRow { Fields = ["A", 1] },
            new TableRow { Fields = ["B", 2] },
        };

        string text = service.BuildMultiRowText(["Col1", "Col2"], selectedItems);

        Assert.Contains("Col1\tCol2", text);
        Assert.Contains("A\t1", text);
        Assert.Contains("B\t2", text);
    }

    [Fact]
    public void BuildSingleCellText_WhenHeaderMapsToString_ReturnsRawValue()
    {
        var service = new DataGridClipboardService();
        var row = new TableRow { Fields = ["raw-text"] };
        var table = new TableOfSqlResults { Headers = ["C1"] };

        string text = service.BuildSingleCellText(row, "C1", table);

        Assert.Equal("raw-text", text);
    }

    [Fact]
    public void BuildSingleCellText_WhenHeaderIndexOutOfBounds_ReturnsEmpty()
    {
        var service = new DataGridClipboardService();
        var row = new TableRow { Fields = ["only-first-column"] };
        var table = new TableOfSqlResults { Headers = ["C1", "C2"] };

        string text = service.BuildSingleCellText(row, "C2", table);

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void BuildCopyWithHeadersText_WithMultipleSelectedItems_ReturnsMultiRowText()
    {
        var service = new DataGridClipboardService();
        var selectedItems = new List<object>
        {
            new TableRow { Fields = ["A", 1] },
            new TableRow { Fields = ["B", 2] },
        };

        string text = service.BuildCopyWithHeadersText(["Col1", "Col2"], selectedItems, selectedItems[0], "Col1", null);

        Assert.Contains("Col1\tCol2", text);
        Assert.Contains("A\t1", text);
        Assert.Contains("B\t2", text);
    }

    [Fact]
    public void BuildCopyWithHeadersText_WithSingleCell_ReturnsCellValue()
    {
        var service = new DataGridClipboardService();
        var row = new TableRow { Fields = ["cell"] };
        var table = new TableOfSqlResults { Headers = ["C1"] };
        var selectedItems = new List<object> { row };

        string text = service.BuildCopyWithHeadersText(["C1"], selectedItems, row, "C1", table);

        Assert.Equal("cell", text);
    }

    [Fact]
    public void BuildCopyWithHeadersText_WithNoSelection_ReturnsEmpty()
    {
        var service = new DataGridClipboardService();

        string text = service.BuildCopyWithHeadersText(["C1"], new List<object>(), null, null, null);

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void BuildSelectedCellsColumnText_WithCells_AppendsOneValuePerLine()
    {
        var service = new DataGridClipboardService();

        string text = service.BuildSelectedCellsColumnText([1, "a", null]);

        string[] lines = text.Split(Environment.NewLine, StringSplitOptions.None);
        Assert.Equal(new[] { "1", "a", "", "" }, lines);
    }

    [Fact]
    public void BuildSelectedCellsColumnText_WithNullCells_ReturnsEmpty()
    {
        var service = new DataGridClipboardService();

        Assert.Equal(string.Empty, service.BuildSelectedCellsColumnText(null));
    }

    [Fact]
    public void BuildSelectedRangeText_WithColumns_RendersHeaderLineAndRows()
    {
        var service = new DataGridClipboardService();
        var rows = new List<TableRow> { new TableRow { Fields = [10, 20, 30] } };

        string text = service.BuildSelectedRangeText(["C1", "C2", "C3"], rows, 0, 1);

        // Header cells are all followed by a tab, data cells only between values.
        Assert.Equal($"C1\tC2\t{Environment.NewLine}10\t20{Environment.NewLine}", text);
    }

    [Fact]
    public void BuildSelectedRangeText_WithReversedColumns_UsesTheLowerIndexFirst()
    {
        var service = new DataGridClipboardService();
        var rows = new List<TableRow> { new TableRow { Fields = [10, 20, 30] } };

        string text = service.BuildSelectedRangeText(["C1", "C2", "C3"], rows, 2, 1);

        Assert.Equal($"C2\tC3\t{Environment.NewLine}20\t30{Environment.NewLine}", text);
    }

    [Fact]
    public void BuildRowValuesText_WithMixedFields_BuildsSqlLiteral()
    {
        var service = new DataGridClipboardService();

        string text = service.BuildRowValuesText(new TableRow { Fields = ["a", 1] });

        Assert.Equal("VALUES ('a',1)", text);
    }

    [Fact]
    public void BuildRowValuesText_WithQuoteAndDbNull_EscapesCorrectly()
    {
        var service = new DataGridClipboardService();

        string text = service.BuildRowValuesText(new TableRow { Fields = ["O'Brien", DBNull.Value] });

        Assert.Equal("VALUES ('O''Brien',NULL)", text);
    }
}
