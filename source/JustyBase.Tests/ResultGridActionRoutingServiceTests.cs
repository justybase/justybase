using JustyBase.Services.DataGrid;

namespace JustyBase.Tests;

public sealed class ResultGridActionRoutingServiceTests
{
    [Theory]
    [InlineData("CopyAsCsvClipboard|button", ResultGridToolbarAction.CopyAsCsvClipboard)]
    [InlineData("CopyAsCsvClipboard|menu", ResultGridToolbarAction.CopyAsCsvClipboard)]
    [InlineData("CopyAsCsvClipboardHeaders|menu", ResultGridToolbarAction.CopyAsCsvClipboardHeaders)]
    [InlineData("CopyAsExcelFileClipboard|button", ResultGridToolbarAction.CopyAsExcelFileClipboard)]
    [InlineData("CopyAsExcelFileClipboard|menu", ResultGridToolbarAction.CopyAsExcelFileClipboard)]
    [InlineData("OpenAsExcelFileClipboard|button", ResultGridToolbarAction.OpenAsExcelFileClipboard)]
    [InlineData("SaveAsExcelFile|menu", ResultGridToolbarAction.SaveAsExcelFile)]
    [InlineData("CopyAsHtml|button", ResultGridToolbarAction.CopyAsHtml)]
    [InlineData("CopySelectecCellsCurrentColumn|menu", ResultGridToolbarAction.CopySelectedCellsCurrentColumn)]
    [InlineData("CopySelectecCellsCurrentColumn2|menu", ResultGridToolbarAction.CopySelectedCellsCurrentColumnRange)]
    [InlineData("CopyRowValues|menu", ResultGridToolbarAction.CopyRowValues)]
    public void Resolve_MapsKnownActions(string action, ResultGridToolbarAction expected)
    {
        var service = new ResultGridActionRoutingService();

        ResultGridToolbarAction result = service.Resolve(action);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("UnknownAction|menu")]
    public void Resolve_ReturnsNone_ForUnknownOrEmpty(string? action)
    {
        var service = new ResultGridActionRoutingService();

        ResultGridToolbarAction result = service.Resolve(action);

        Assert.Equal(ResultGridToolbarAction.None, result);
    }

    [Fact]
    public void RequiresTableReader_ReturnsFalseOnlyForNone()
    {
        var service = new ResultGridActionRoutingService();

        Assert.False(service.RequiresTableReader(ResultGridToolbarAction.None));
        Assert.True(service.RequiresTableReader(ResultGridToolbarAction.CopyAsCsvClipboard));
    }
}
