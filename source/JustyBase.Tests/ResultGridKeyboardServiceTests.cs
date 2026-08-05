using Avalonia.Input;
using JustyBase.Services.DataGrid;

namespace JustyBase.Tests;

public class ResultGridKeyboardServiceTests
{
    private readonly ResultGridKeyboardService _service;

    public ResultGridKeyboardServiceTests()
    {
        _service = new ResultGridKeyboardService();
    }

    [Fact]
    public void ParseKeyDown_WithCtrlC_ReturnsCopy()
    {
        var result = _service.ParseKeyDown(Key.C, KeyModifiers.Control);
        Assert.Equal(ResultGridKeyboardAction.Copy, result);
    }

    [Fact]
    public void ParseKeyDown_WithCtrlA_ReturnsCopyAll()
    {
        var result = _service.ParseKeyDown(Key.A, KeyModifiers.Control);
        Assert.Equal(ResultGridKeyboardAction.CopyAll, result);
    }

    [Fact]
    public void ParseKeyDown_WithCtrlOtherKey_ReturnsNone()
    {
        var result = _service.ParseKeyDown(Key.V, KeyModifiers.Control);
        Assert.Equal(ResultGridKeyboardAction.None, result);
    }

    [Fact]
    public void ParseKeyDown_WithNoModifiers_ReturnsNone()
    {
        var result = _service.ParseKeyDown(Key.C, KeyModifiers.None);
        Assert.Equal(ResultGridKeyboardAction.None, result);
    }

    [Fact]
    public void ParseKeyDown_WithShiftModifier_ReturnsNone()
    {
        var result = _service.ParseKeyDown(Key.C, KeyModifiers.Shift);
        Assert.Equal(ResultGridKeyboardAction.None, result);
    }
}
