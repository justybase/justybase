using JustyBase.Services.Actions;

namespace JustyBase.Tests;

public sealed class ActionRegistryTests
{
    [Fact]
    public void Add_RejectsDuplicateIds()
    {
        var registry = new ActionRegistry();
        registry.Add(new IdeAction("a", "A", "Group", _ => Task.CompletedTask));

        Assert.Throws<InvalidOperationException>(
            () => registry.Add(new IdeAction("a", "Other", "Group", _ => Task.CompletedTask)));
    }

    [Fact]
    public void Search_MatchesEveryWordAcrossTitleGroupAndId()
    {
        var registry = new ActionRegistry();
        registry.Add(new IdeAction("document.save", "Save Document", "File", _ => Task.CompletedTask));
        registry.Add(new IdeAction("document.format", "Format SQL", "SQL", _ => Task.CompletedTask));

        Assert.Single(registry.Search("save"));
        Assert.Single(registry.Search("file save"));
        Assert.Single(registry.Search("document format"));
        Assert.Equal(2, registry.Search(string.Empty).Count);
    }

    [Fact]
    public async Task RunAsync_HonoursCanRunAndRaisesExecuted()
    {
        var registry = new ActionRegistry();
        int runs = 0;
        bool allowed = false;
        registry.Add(new IdeAction(
            "action",
            "Action",
            "Group",
            _ =>
            {
                runs++;
                return Task.CompletedTask;
            },
            _ => allowed));

        IdeAction? executed = null;
        registry.Executed += (_, action) => executed = action;

        Assert.False(await registry.RunAsync("action"));
        Assert.Equal(0, runs);

        allowed = true;
        Assert.True(await registry.RunAsync("action"));
        Assert.Equal(1, runs);
        Assert.NotNull(executed);
    }

    [Fact]
    public async Task RunAsync_UnknownId_ReturnsFalse()
    {
        var registry = new ActionRegistry();
        Assert.False(await registry.RunAsync("missing"));
    }

    [Fact]
    public void CanExecute_WhenPredicateThrows_ReturnsFalse()
    {
        var action = new IdeAction(
            "action",
            "Action",
            "Group",
            _ => Task.CompletedTask,
            _ => throw new InvalidOperationException("boom"));

        Assert.False(action.CanExecute(new ActionContext()));
    }

    [Fact]
    public void WhyNot_ExplainsARefusedContext()
    {
        var action = new IdeAction(
            "action",
            "Action",
            "Group",
            _ => Task.CompletedTask,
            _ => false,
            WhyNot: _ => "Open a document first.");

        Assert.False(action.CanExecute(new ActionContext()));
        Assert.Equal("Open a document first.", action.GetWhyNot(new ActionContext()));
    }

    [Fact]
    public void WhyNot_IsNullWhenTheActionCanRun()
    {
        var action = new IdeAction(
            "action",
            "Action",
            "Group",
            _ => Task.CompletedTask,
            _ => true,
            WhyNot: _ => "never shown");

        Assert.True(action.CanExecute(new ActionContext()));
        Assert.Null(action.GetWhyNot(new ActionContext()));
    }
}
