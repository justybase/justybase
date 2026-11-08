using Avalonia.Input;
using JustyBase.Services.Actions;

namespace JustyBase.Tests;

public sealed class KeymapServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "JustyBase.Tests",
        "keymap",
        Guid.NewGuid().ToString("N"));

    public KeymapServiceTests()
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

    private string FilePath => Path.Combine(_folder, "keymap.json");

    private static ActionRegistry CreateRegistry()
    {
        var registry = new ActionRegistry();
        registry.Add(new IdeAction(
            "one",
            "First Action",
            "Test",
            _ => Task.CompletedTask,
            Shortcut: "Ctrl+K"));
        registry.Add(new IdeAction(
            "two",
            "Second Action",
            "Test",
            _ => Task.CompletedTask,
            Shortcut: "Ctrl+J"));
        return registry;
    }

    [Fact]
    public void GetGesture_ReturnsDeclaredDefault()
    {
        var keymap = new KeymapService(CreateRegistry(), FilePath);

        Assert.Equal("Ctrl+K", keymap.GetGesture("one"));
        Assert.Equal("Ctrl+J", keymap.GetGesture("two"));
    }

    [Fact]
    public async Task SetGesture_PersistsAcrossInstances()
    {
        ActionRegistry registry = CreateRegistry();
        var keymap = new KeymapService(registry, FilePath);

        KeymapUpdateResult result = await keymap.SetGestureAsync("one", "Ctrl+Shift+K");

        Assert.True(result.Success);
        Assert.NotNull(result.Gesture);

        var reopened = new KeymapService(registry, FilePath);
        Assert.Equal(result.Gesture, reopened.GetGesture("one"));
    }

    [Fact]
    public async Task SetGesture_RefusesAConflictAndKeepsTheOldBinding()
    {
        var keymap = new KeymapService(CreateRegistry(), FilePath);

        KeymapUpdateResult result = await keymap.SetGestureAsync("one", "Ctrl+J");

        Assert.False(result.Success);
        Assert.NotEmpty(result.Conflicts);
        Assert.Contains(result.Conflicts[0].Actions, action => action.Id == "two");
        Assert.Equal("Ctrl+K", keymap.GetGesture("one"));
    }

    [Fact]
    public async Task SetGestureRefusesGesturesOwnedByTheShell()
    {
        var keymap = new KeymapService(CreateRegistry(), FilePath);

        KeymapUpdateResult result = await keymap.SetGestureAsync("one", "Ctrl+T");

        Assert.False(result.Success);
        Assert.Contains("reserved", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Ctrl+K", keymap.GetGesture("one"));
    }

    [Fact]
    public async Task SetGesture_EmptyRemovesTheBinding()
    {
        var keymap = new KeymapService(CreateRegistry(), FilePath);

        KeymapUpdateResult result = await keymap.SetGestureAsync("one", "");

        Assert.True(result.Success);
        Assert.Null(result.Gesture);
        Assert.Null(keymap.GetGesture("one"));
    }

    [Fact]
    public async Task SetGesture_RejectsInvalidText()
    {
        var keymap = new KeymapService(CreateRegistry(), FilePath);

        KeymapUpdateResult result = await keymap.SetGestureAsync("one", "not a key");

        Assert.False(result.Success);
        Assert.Equal("Ctrl+K", keymap.GetGesture("one"));
    }

    [Fact]
    public async Task SetGestureWhenSaveFailsRollsBackTheInMemoryOverride()
    {
        string blockedParent = Path.Combine(_folder, "blocked");
        await File.WriteAllTextAsync(blockedParent, "not a directory");
        var keymap = new KeymapService(
            CreateRegistry(),
            Path.Combine(blockedParent, "keymap.json"));

        await Assert.ThrowsAnyAsync<IOException>(
            () => keymap.SetGestureAsync("one", "Ctrl+Shift+K"));

        Assert.Equal("Ctrl+K", keymap.GetGesture("one"));
    }

    [Fact]
    public async Task Reset_RestoresTheDeclaredDefault()
    {
        var keymap = new KeymapService(CreateRegistry(), FilePath);
        await keymap.SetGestureAsync("one", "Ctrl+Shift+K");

        await keymap.ResetAsync("one");

        Assert.Equal("Ctrl+K", keymap.GetGesture("one"));
    }

    [Fact]
    public void FindConflicts_GroupsActionsThatShareAGesture()
    {
        var registry = new ActionRegistry();
        registry.Add(new IdeAction("a", "A", "Test", _ => Task.CompletedTask, Shortcut: "Ctrl+K"));
        registry.Add(new IdeAction("b", "B", "Test", _ => Task.CompletedTask, Shortcut: "Ctrl+K"));
        var keymap = new KeymapService(registry, FilePath);

        KeymapConflict conflict = Assert.Single(keymap.FindConflicts());

        Assert.Equal("Ctrl+K", conflict.Gesture);
        Assert.Equal(2, conflict.Actions.Count);
    }

    [Fact]
    public void GetAll_ReportsWhetherTheBindingIsCustom()
    {
        var registry = CreateRegistry();
        var keymap = new KeymapService(registry, FilePath);

        Assert.All(keymap.GetAll(), entry => Assert.False(entry.IsCustom));
        Assert.Equal(2, keymap.GetAll().Count);
    }

    [Fact]
    public void EveryDeclaredShortcut_IsAValidKeyGesture()
    {
        var registry = new ActionRegistry();
        SqlDocumentActions.Register(registry);
        WorkspaceActions.Register(registry);

        foreach (IdeAction action in registry.All)
        {
            if (string.IsNullOrWhiteSpace(action.Shortcut))
            {
                continue;
            }

            KeyGesture parsed = KeyGesture.Parse(action.Shortcut);
            Assert.NotNull(parsed.Key);
        }
    }
}
