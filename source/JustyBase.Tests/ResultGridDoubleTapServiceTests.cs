using JustyBase.Models;
using JustyBase.Services.DataGrid;
using JustyBase.ViewModels.Tools;

namespace JustyBase.Tests;

public sealed class ResultGridDoubleTapServiceTests
{
    [Theory]
    [InlineData(true, false, false, false, true)]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, false, false, true, false)]
    public void ShouldHandleHeaderDoubleTap_ReturnsExpected(
        bool isHeaderClicked,
        bool isRowDetailsGrid,
        bool sourceIsSqlResultsViewModel,
        bool sourceIsDataGridCollectionViewGroup,
        bool expected)
    {
        var service = new ResultGridDoubleTapService();

        bool result = service.ShouldHandleHeaderDoubleTap(
            isHeaderClicked,
            isRowDetailsGrid,
            sourceIsSqlResultsViewModel,
            sourceIsDataGridCollectionViewGroup);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetHeaderDoubleTapValue_WhenContextIsNull_ReturnsNull()
    {
        var service = new ResultGridDoubleTapService();

        var result = service.GetHeaderDoubleTapValue(null);

        Assert.Null(result);
    }

    [Fact]
    public void GetHeaderDoubleTapValue_WhenContextHasToString_ReturnsStringValue()
    {
        var service = new ResultGridDoubleTapService();

        var result = service.GetHeaderDoubleTapValue(123);

        Assert.Equal("123", result);
    }

    [Fact]
    public void GetTableRowDoubleTapValue_ReturnsSelectedField()
    {
        var service = new ResultGridDoubleTapService();
        var row = new TableRow { Fields = [10, "abc", 30] };

        var result = service.GetTableRowDoubleTapValue(row, 1);

        Assert.Equal("abc", result);
    }

    [Fact]
    public void GetRowDetailDoubleTapPayload_WhenDisplayIndexZero_ReturnsNameAndRawMode()
    {
        var service = new ResultGridDoubleTapService();
        var rowDetail = new RowDetail
        {
            Name = "COL_A",
            FieldsValues = ["A1", "A2"],
            TypeName = "VARCHAR"
        };

        ResultGridDoubleTapPayload payload = service.GetRowDetailDoubleTapPayload(rowDetail, currentColumnDisplayIndex: 0, columnsCount: 4);

        Assert.Equal("COL_A", payload.Value);
        Assert.True(payload.RawMode);
    }

    [Fact]
    public void GetRowDetailDoubleTapPayload_WhenValueColumn_ReturnsFieldValue()
    {
        var service = new ResultGridDoubleTapService();
        var rowDetail = new RowDetail
        {
            Name = "COL_A",
            FieldsValues = ["A1", "A2"],
            TypeName = "VARCHAR"
        };

        ResultGridDoubleTapPayload payload = service.GetRowDetailDoubleTapPayload(rowDetail, currentColumnDisplayIndex: 2, columnsCount: 4);

        Assert.Equal("A2", payload.Value);
        Assert.False(payload.RawMode);
    }

    [Fact]
    public void GetRowDetailDoubleTapPayload_WhenTypeColumn_ReturnsTypeName()
    {
        var service = new ResultGridDoubleTapService();
        var rowDetail = new RowDetail
        {
            Name = "COL_A",
            FieldsValues = ["A1", "A2"],
            TypeName = "VARCHAR"
        };

        ResultGridDoubleTapPayload payload = service.GetRowDetailDoubleTapPayload(rowDetail, currentColumnDisplayIndex: 3, columnsCount: 4);

        Assert.Equal("VARCHAR", payload.Value);
        Assert.False(payload.RawMode);
    }
}
