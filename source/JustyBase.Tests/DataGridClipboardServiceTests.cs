using JustyBase.Models;
using JustyBase.Services.DataGrid;
using System.Collections.Generic;

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
}
