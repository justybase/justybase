using Avalonia.Threading;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.QuickOpen;
using JustyBase.Services.Actions;
using JustyBase.Services.History;
using JustyBase.ViewModels;
using JustyBase.Views.OtherDialogs;
using Moq;

namespace JustyBase.HeadlessTests;

/// <summary>
/// Four small, deterministic windows that exercise the parts most likely to regress visually:
/// Go to Everything, Local History, the keymap editor and the command palette. Baselines live next
/// to this file and are refreshed with JUSTYBASE_UPDATE_SNAPSHOTS=1.
/// </summary>
public sealed class VisualRegressionTests : HeadlessSessionTestBase
{
    [Fact]
    public Task GoToEverything_RendersEverySection() => RunOnUiAsync(async () =>
    {
        QuickOpenCandidate[] candidates =
        [
            new("customer_report.sql", @"C:\scripts\customer_report.sql", null,
                "SELECT * FROM customer;", QuickOpenSource.Files, "scripts"),
            new("sales_summary.sql", @"C:\scripts\sales_summary.sql", null,
                "SELECT * FROM sales;", QuickOpenSource.Files, "scripts"),
        ];

        var vm = new QuickOpenViewModel(
            new QuickOpenSearchService(),
            candidates,
            TimeSpan.FromSeconds(2),
            closeCancel: () => { },
            closeAccept: _ => { },
            initialQuery: "customer",
            gotoPosition: null,
            symbolSearch: (_, _) => Task.FromResult<IReadOnlyList<QuickOpenHit>>(
            [
                new QuickOpenHit(QuickOpenHitKind.Symbol, "customer_id", "column  customer_report.sql:4",
                    @"C:\scripts\customer_report.sql", null, QuickOpenSource.Files, 90, "customer",
                    LineNumber: 4, MatchIndex: 8, MatchLength: 11),
            ]),
            searchEverything: true,
            actionSearch: (_, _) => Task.FromResult<IReadOnlyList<QuickOpenHit>>(
            [
                new QuickOpenHit(QuickOpenHitKind.Action, "Open SQL File…", "File  ·  Ctrl+O",
                    null, null, QuickOpenSource.None, 20, "customer",
                    ActionId: WorkspaceActions.OpenFile),
            ]),
            schemaSearch: (_, _) => Task.FromResult<IReadOnlyList<QuickOpenHit>>(
            [
                new QuickOpenHit(QuickOpenHitKind.SchemaObject, "CUSTOMER", "table  ·  JUST_DATA.ADMIN",
                    null, null, QuickOpenSource.None, 10, "customer",
                    InsertText: "JUST_DATA.ADMIN.CUSTOMER"),
                new QuickOpenHit(QuickOpenHitKind.SchemaObject, "CUSTOMER_ID", "column  ·  JUST_DATA.ADMIN.CUSTOMER",
                    null, null, QuickOpenSource.None, 8, "customer",
                    InsertText: "CUSTOMER_ID"),
            ]),
            historySearch: (_, _) => Task.FromResult<IReadOnlyList<QuickOpenHit>>(
            [
                new QuickOpenHit(QuickOpenHitKind.History, "SELECT * FROM CUSTOMER",
                    "2026-11-08 14:05:22  ·  JustData  ·  JUST_DATA  ·  OK  ·  123 ms",
                    null, null, QuickOpenSource.None, 5, "customer",
                    Snippet: "SELECT * FROM CUSTOMER WHERE customer_id = 1;",
                    Payload: "SELECT * FROM CUSTOMER WHERE customer_id = 1;"),
            ]));

        var window = new QuickOpenWindow(vm)
        {
            Width = 760,
            Height = 520,
        };
        window.Show();
        await Task.Delay(900);
        Dispatcher.UIThread.RunJobs();

        VisualRegression.AssertMatches("go-to-everything", window);
        window.Close();
    });

    [Fact]
    public Task LocalHistory_RendersVersionsAndLabels() => RunOnUiAsync(async () =>
    {
        string folder = Path.Combine(
            Path.GetTempPath(),
            "JustyBase.VisualTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "customer_report.sql");
        await File.WriteAllTextAsync(file, "SELECT 2;");

        using var history = new LocalHistoryService(
            Path.Combine(folder, "history"),
            clock: CreateClock());
        LocalHistoryEntry first = Assert.IsType<LocalHistoryEntry>(
            await history.RecordAsync(file, "SELECT 1;", "open"));
        await history.SetLabelAsync(first.Id, "before cleanup");
        await history.RecordAsync(file, "SELECT 2;", "save");
        await history.RecordAsync(file, "SELECT 3;", "save");

        var window = new LocalHistoryWindow(
            history,
            Mock.Of<IMessageForUserTools>(),
            file)
        {
            Width = 1060,
            Height = 640,
        };
        window.Show();
        await window.LoadedTask.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();

        VisualRegression.AssertMatches("local-history", window);
        window.Close();
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    });

    [Fact]
    public Task KeymapEditor_RendersActionsAndShortcuts() => RunOnUi(() =>    {
        var registry = new ActionRegistry();
        registry.Add(new IdeAction("document.save", "Save Document", "File", _ => Task.CompletedTask,
            Shortcut: "Ctrl+S"));
        registry.Add(new IdeAction("document.formatSql", "Format SQL", "SQL", _ => Task.CompletedTask,
            Shortcut: "Ctrl+Alt+F"));
        registry.Add(new IdeAction("workspace.goToEverything", "Go to Everything…", "Navigation",
            _ => Task.CompletedTask, Shortcut: "Ctrl+Shift+O"));
        registry.Add(new IdeAction("workspace.keymap", "Edit Keyboard Shortcuts…", "View",
            _ => Task.CompletedTask));

        string folder = Path.Combine(
            Path.GetTempPath(),
            "JustyBase.VisualTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var keymap = new KeymapService(registry, Path.Combine(folder, "keymap.json"));

        var window = new KeymapWindow(keymap)
        {
            Width = 760,
            Height = 620,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        VisualRegression.AssertMatches("keymap-editor", window);
        window.Close();
        Directory.Delete(folder, recursive: true);
    });

    [Fact]
    public Task ActionPalette_ExplainsWhyAnActionCannotRun() => RunOnUi(() =>
    {
        var registry = new ActionRegistry();
        registry.Add(new IdeAction(
            "document.save",
            "A Save Action (no editor)",
            "File",
            _ => Task.CompletedTask,
            _ => false,
            Shortcut: "Ctrl+S",
            WhyNot: _ => "Open a SQL document first."));
        registry.Add(new IdeAction(
            "workspace.goToEverything",
            "Z Go to Everything",
            "Navigation",
            _ => Task.CompletedTask,
            Shortcut: "Ctrl+Shift+O"));

        var window = new ActionPaletteWindow(registry, new ActionContext(), _ => { })
        {
            Width = 640,
            Height = 460,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        VisualRegression.AssertMatches("action-palette", window);
        window.Close();
    });

    private static Func<DateTimeOffset> CreateClock()
    {
        var local = new DateTime(2026, 11, 8, 14, 5, 22, DateTimeKind.Unspecified);
        var start = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        int seconds = 0;
        return () => start.AddSeconds(seconds++);
    }
}
