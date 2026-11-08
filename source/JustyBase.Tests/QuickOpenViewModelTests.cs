using JustyBase.QuickOpen;
using JustyBase.ViewModels;

namespace JustyBase.Tests;

public sealed class QuickOpenViewModelTests
{
    [Fact]
    public void AcceptSelection_ParsesLineAndColumn()
    {
        QuickOpenGotoTarget? target = null;
        var vm = CreateViewModel(":42:7", value => target = value);

        vm.AcceptSelection();

        Assert.Equal(new QuickOpenGotoTarget(42, 7), target);
    }

    [Fact]
    public void AcceptSelection_BareLineTargetsFirstColumn()
    {
        QuickOpenGotoTarget? target = null;
        var vm = CreateViewModel(":42", value => target = value);

        vm.AcceptSelection();

        Assert.Equal(new QuickOpenGotoTarget(42, 1), target);
    }

    [Fact]
    public void AcceptSelection_CommaAlsoSeparatesColumn()
    {
        QuickOpenGotoTarget? target = null;
        var vm = CreateViewModel(":42,7", value => target = value);

        vm.AcceptSelection();

        Assert.Equal(new QuickOpenGotoTarget(42, 7), target);
    }

    [Fact]
    public void AcceptSelection_InvalidPositionDoesNotNavigate()
    {
        QuickOpenGotoTarget? target = null;
        var vm = CreateViewModel(":0:0", value => target = value);

        vm.AcceptSelection();

        Assert.Null(target);
    }

    private static QuickOpenViewModel CreateViewModel(
        string query,
        Action<QuickOpenGotoTarget> gotoPosition)
        => new(
            new QuickOpenSearchService(),
            [],
            TimeSpan.FromSeconds(1),
            closeCancel: () => { },
            closeAccept: _ => { },
            initialQuery: query,
            gotoPosition: gotoPosition);
}
