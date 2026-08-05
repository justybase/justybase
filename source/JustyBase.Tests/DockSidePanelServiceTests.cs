using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using JustyBase.Services.Docking;
using JustyBase.ViewModels.Views;

namespace JustyBase.Tests;

public sealed class DockSidePanelServiceTests
{
    [Fact]
    public void HideSideElements_RemovesLeftSideDockAndKeepsMiddle()
    {
        var service = new DockSidePanelService();
        var middleDock = new ProportionalDock { Title = "MiddleDock" };
        var sideDock = new ProportionalDock { Title = "SideDock" };
        var splitter = new ProportionalDockSplitter();
        var layoutDock = new ProportionalDock { VisibleDockables = [sideDock, splitter, middleDock] };
        List<IDockable> hiddenDockables = [];

        service.HideSideElements(layoutDock, middleDock, hiddenDockables);

        Assert.Single(layoutDock.VisibleDockables!);
        Assert.Same(middleDock, layoutDock.VisibleDockables[0]);
        Assert.Equal(2, hiddenDockables.Count);
        Assert.Contains(sideDock, hiddenDockables);
        Assert.Contains(splitter, hiddenDockables);
    }

    [Fact]
    public void HideSideElements_KeepsAiChatDockOnRight()
    {
        var service = new DockSidePanelService();
        var middleDock = new ProportionalDock { Title = "MiddleDock" };
        var sideDock = new ProportionalDock { Title = "SideDock" };
        var aiChatDock = new ToolDock { Id = "AiChatDock", Title = "AI Chat" };
        var sideSplitter = new ProportionalDockSplitter();
        var aiSplitter = new ProportionalDockSplitter();
        var layoutDock = new ProportionalDock
        {
            VisibleDockables =
            [
                sideDock,
                sideSplitter,
                middleDock,
                aiSplitter,
                aiChatDock
            ]
        };
        List<IDockable> hiddenDockables = [];

        service.HideSideElements(layoutDock, middleDock, hiddenDockables);

        Assert.Equal(3, layoutDock.VisibleDockables!.Count);
        Assert.Same(middleDock, layoutDock.VisibleDockables[0]);
        Assert.Same(aiSplitter, layoutDock.VisibleDockables[1]);
        Assert.Same(aiChatDock, layoutDock.VisibleDockables[2]);
        Assert.Contains(sideDock, hiddenDockables);
        Assert.Contains(sideSplitter, hiddenDockables);
        Assert.DoesNotContain(aiSplitter, hiddenDockables);
    }

    [Fact]
    public void HideSideElements_DoesNotReplaceVisibleDockablesCollection()
    {
        var service = new DockSidePanelService();
        var middleDock = new ProportionalDock { Title = "MiddleDock" };
        var sideDock = new ProportionalDock { Title = "SideDock" };
        var splitter = new ProportionalDockSplitter();
        IList<IDockable> original = [sideDock, splitter, middleDock];
        var layoutDock = new ProportionalDock { VisibleDockables = original };
        List<IDockable> hiddenDockables = [];

        service.HideSideElements(layoutDock, middleDock, hiddenDockables);

        Assert.Same(original, layoutDock.VisibleDockables);
    }

    [Fact]
    public void RestoreSideElements_InsertsSideDockOnLeftWithoutReplacingCollection()
    {
        var service = new DockSidePanelService();
        var middleDock = new ProportionalDock { Title = "MiddleDock" };
        var sideDock = new ProportionalDock { Title = "SideDock", Proportion = 0.25 };
        var aiChatDock = new ToolDock { Id = "AiChatDock", Title = "AI Chat" };
        IList<IDockable> original =
        [
            middleDock,
            new ProportionalDockSplitter(),
            aiChatDock
        ];
        var layoutDock = new ProportionalDock { VisibleDockables = original };
        List<IDockable> hiddenDockables = [sideDock, new ProportionalDockSplitter()];

        service.RestoreSideElements(layoutDock, middleDock, hiddenDockables);

        Assert.Same(original, layoutDock.VisibleDockables);
        Assert.Equal(5, layoutDock.VisibleDockables!.Count);
        Assert.Same(sideDock, layoutDock.VisibleDockables[0]);
        Assert.IsType<ProportionalDockSplitter>(layoutDock.VisibleDockables[1]);
        Assert.Same(middleDock, layoutDock.VisibleDockables[2]);
        Assert.Same(aiChatDock, layoutDock.VisibleDockables[4]);
        Assert.Empty(hiddenDockables);
    }

    [Fact]
    public void ResolveMiddleDock_ReturnsMiddleDockFromRootWhenCacheIsMissing()
    {
        var service = new DockSidePanelService();
        var middleDock = new ProportionalDock { Title = "MiddleDock" };
        var mainLayout = new ProportionalDock { VisibleDockables = [new ProportionalDockSplitter(), middleDock] };
        var mainViewModel = new MainViewModel { ActiveDockable = mainLayout, VisibleDockables = [mainLayout] };
        var rootDock = new RootDock { ActiveDockable = mainViewModel, VisibleDockables = [mainViewModel] };

        var result = service.ResolveMiddleDock(rootDock, cachedMiddleDock: null);

        Assert.Same(middleDock, result);
    }
}
