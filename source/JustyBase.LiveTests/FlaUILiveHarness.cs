using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using FlaUIApplication = FlaUI.Core.Application;

namespace JustyBase.LiveTests;

internal static class FlaUILiveHarness
{
    public static readonly TimeSpan AppStartTimeout = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(1.2);

    /// <summary>
    /// Prep for live capture without touching other apps.
    /// Maximizes Justy Base and widens the left tool panel so Schema Search Refresh is visible.
    /// Never sends global hotkeys and never minimizes foreign windows.
    /// </summary>
    public static void MinimizeAllWindowsThenFocus(Window main)
    {
        EnsureJustyBaseForeground(main);
        TryMaximize(main);
        WidenLeftToolPanel(main);
        EnsureJustyBaseForeground(main);
        Thread.Sleep(400);
    }

    public static void TryMaximize(Window main)
    {
        try
        {
            if (main.Patterns.Window.IsSupported)
            {
                main.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
            }
        }
        catch
        {
        }

        Thread.Sleep(300);
    }

    /// <summary>
    /// Drag the vertical dock splitter so Schema / Schema Search is wide enough for Refresh.
    /// All mouse points are clamped inside Justy Base — never leave the window (avoids Keep/desktop).
    /// </summary>
    public static void WidenLeftToolPanel(Window main)
    {
        EnsureJustyBaseForeground(main);

        var leftPane = Flatten(main).FirstOrDefault(e =>
                            e.ControlType == ControlType.Pane
                            && string.Equals(e.Name, "Schema search", StringComparison.OrdinalIgnoreCase))
                        ?? Flatten(main).FirstOrDefault(e =>
                            e.ControlType == ControlType.Pane
                            && string.Equals(e.Name, "Schema", StringComparison.OrdinalIgnoreCase));
        if (leftPane is null)
        {
            return;
        }

        System.Drawing.Rectangle paneBounds;
        System.Drawing.Rectangle mainBounds;
        try
        {
            paneBounds = leftPane.BoundingRectangle;
            mainBounds = main.BoundingRectangle;
        }
        catch
        {
            return;
        }

        if (paneBounds.Width <= 0 || mainBounds.Width <= 0)
        {
            return;
        }

        if (paneBounds.Width >= Math.Max(420, (int)(mainBounds.Width * 0.34)))
        {
            return;
        }

        var splitter = Flatten(main)
            .Where(e => e.ControlType == ControlType.Thumb)
            .Select(t =>
            {
                try { return (Element: (AutomationElement?)t, Bounds: t.BoundingRectangle); }
                catch { return (Element: (AutomationElement?)null, Bounds: System.Drawing.Rectangle.Empty); }
            })
            .Where(x => x.Element is not null && x.Bounds.Width > 0 && x.Bounds.Height > 20)
            .Where(x => Math.Abs(x.Bounds.X - paneBounds.Right) <= 24
                        || (x.Bounds.X >= paneBounds.Right - 8 && x.Bounds.X <= paneBounds.Right + 16))
            .OrderBy(x => Math.Abs(x.Bounds.X - paneBounds.Right))
            .Select(x => x.Element!)
            .FirstOrDefault();

        if (splitter is null)
        {
            return;
        }

        try
        {
            var sb = splitter.BoundingRectangle;
            var from = ClampPointToWindow(
                new System.Drawing.Point(sb.X + Math.Max(1, sb.Width / 2), sb.Y + Math.Max(1, sb.Height / 2)),
                mainBounds);
            var to = ClampPointToWindow(
                new System.Drawing.Point(
                    Math.Max(from.X + 280, mainBounds.X + (int)(mainBounds.Width * 0.38)),
                    from.Y),
                mainBounds);
            if (to.X <= from.X + 40)
            {
                return;
            }

            EnsureJustyBaseForeground(main);
            Mouse.Position = from;
            Thread.Sleep(50);
            EnsureJustyBaseForeground(main);
            Mouse.Down(MouseButton.Left);
            Thread.Sleep(30);
            // Step drag — stay inside Justy Base bounds every step.
            const int steps = 12;
            for (int i = 1; i <= steps; i++)
            {
                EnsureJustyBaseForeground(main);
                int x = from.X + (to.X - from.X) * i / steps;
                int y = from.Y + (to.Y - from.Y) * i / steps;
                Mouse.Position = ClampPointToWindow(new System.Drawing.Point(x, y), mainBounds);
                Thread.Sleep(20);
            }

            Mouse.Up(MouseButton.Left);
            Thread.Sleep(400);
            EnsureJustyBaseForeground(main);
        }
        catch
        {
            try { Mouse.Up(MouseButton.Left); } catch { /* ignore */ }
        }
    }

    private static System.Drawing.Point ClampPointToWindow(System.Drawing.Point p, System.Drawing.Rectangle mainBounds)
    {
        const int pad = 8;
        int x = Math.Clamp(p.X, mainBounds.Left + pad, mainBounds.Right - pad);
        int y = Math.Clamp(p.Y, mainBounds.Top + pad, mainBounds.Bottom - pad);
        return new System.Drawing.Point(x, y);
    }

    /// <summary>
    /// Hard gate for keyboard input: Justy Base process must own the foreground window.
    /// Does not AttachThreadInput to other apps (that previously yanked Keep into the input path).
    /// </summary>
    public static void EnsureJustyBaseForeground(Window main)
    {
        var hwnd = GetNativeHwnd(main);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("Cannot resolve Justy Base HWND — refusing keyboard input.");
        }

        _ = GetWindowThreadProcessId(hwnd, out uint ourPid);

        for (int attempt = 0; attempt < 15; attempt++)
        {
            ForceForegroundWindow(hwnd);
            try
            {
                main.SetForeground();
                main.Focus();
            }
            catch
            {
            }

            if (IsJustyBaseProcessForeground(ourPid, hwnd))
            {
                return;
            }

            Thread.Sleep(100);
        }

        throw new InvalidOperationException(
            "Refusing keyboard/clipboard paste: Justy Base is not the foreground window. "
            + "This protects other desktop apps (e.g. Keep) from accidental Ctrl+V.");
    }

    public static void TryRestoreAndFocus(Window main)
    {
        try
        {
            EnsureJustyBaseForeground(main);
        }
        catch
        {
            try
            {
                var hwnd = GetNativeHwnd(main);
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, SW_RESTORE);
                    ForceForegroundWindow(hwnd);
                }
            }
            catch
            {
            }
        }
    }

    private static bool IsJustyBaseProcessForeground(uint ourPid, IntPtr ourHwnd)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(fg, out uint fgPid);
        if (ourPid != 0 && fgPid == ourPid)
        {
            return true;
        }

        if (fg == ourHwnd)
        {
            return true;
        }

        return GetAncestor(fg, GA_ROOT) == ourHwnd;
    }

    private static void ForceForegroundWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // No AttachThreadInput to foreign apps — that was pulling Keep into the focus chain.
        ShowWindow(hwnd, SW_RESTORE);
        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
    }

    public static readonly string[] SchemaExpandTokens =
        ["JUST_DATA", "DIM_DATE", "DIMDATE", "Tables", "TABLE", "SAMPLE", "NZ"];

    /// <summary>
    /// Safe "advanced" SQL inspired by pictures/picture_1_2.sql (CTE/JOIN look, no DELETE/DROP/CREATE).
    /// </summary>
    public static readonly string AdvancedEvo1Sql = """
        WITH CTE1 AS (
            SELECT 1 AS COL, 11 AS COL2
        )
        , CTE2 AS (
            SELECT 2 AS COL, 22 AS COL2
        )
        --REGION CTE3
        , CTE3 AS (
            SELECT 2 AS COL, 33 AS COL3
        )
        --ENDREGION
        SELECT
            D.*
        FROM
            JUST_DATA..DIMDATE D
            JOIN CTE1 C1 ON C1.COL = 1 OR 2 = 2
            JOIN CTE2 AS C2 ON C2.COL2 = 22
            JOIN CTE3 ON CTE3.COL3 = 33
        WHERE
            1 = 1;
        """;

    /// <summary>
    /// README evo1 scene: expanded Schema, refreshed Schema Search with column filter open,
    /// advanced SQL in editor, and SQL results visible. Document stays active (not History).
    /// Returns true when a results grid with rows is present.
    /// </summary>
    public static bool PrepareRichEvo1Scene(Window main, UIA3Automation automation, string? intermediateShotDir = null)
    {
        TryRestoreAndFocus(main);
        Thread.Sleep(300);
        TryMaximize(main);
        WidenLeftToolPanel(main);
        DismissMessageDialogs(automation);

        // Extra document tabs for dense chrome (UIA only — no coordinate clicks outside controls).
        for (int i = 0; i < 3; i++)
        {
            if (!TryCreateNewDocumentTab(main, automation))
            {
                break;
            }

            Thread.Sleep(300);
        }

        EnsureJustyBaseForeground(main);
        WidenLeftToolPanel(main);

        // Schema Search: refresh + open column filters + filter Name.
        _ = TryClick(FindByNameContains(main, "Schema search"))
            || TryClick(FindByNameContains(main, "Schema Search"));
        Thread.Sleep(400);
        WidenLeftToolPanel(main);

        var refresh = FindByNameOrId(main, automation, "SchemaSearchRefresh")
                      ?? FindByNameContains(main, "Refresh");
        Assert.True(TryClick(refresh), "SchemaSearchRefresh not found/clickable — widen left panel failed?");
        WaitUntilSchemaSearchIdle(main, automation, TimeSpan.FromSeconds(90));
        DismissMessageDialogs(automation);

        if (!string.IsNullOrEmpty(intermediateShotDir))
        {
            SaveShot(main, Path.Combine(intermediateShotDir, "06a-schema-search-refreshed.png"));
        }

        _ = TryClick(FindByNameOrId(main, automation, "SchemaSearchShowFilter"));
        Thread.Sleep(300);

        var nameFilter = FindByNameOrId(main, automation, "SchemaSearchFilterName");
        if (nameFilter is not null)
        {
            SetControlText(main, nameFilter, "DIMDATE");
            Thread.Sleep(600);
        }

        if (!string.IsNullOrEmpty(intermediateShotDir))
        {
            SaveShot(main, Path.Combine(intermediateShotDir, "06b-schema-search-filter.png"));
        }

        // Expand Schema tree toward JUST_DATA / DIMDATE.
        EnsureJustyBaseForeground(main);
        _ = TryClick(FindByNameContains(main, "Schema"))
            || TryClick(FindByNameOrId(main, automation, "DbSchemaTree"));
        Thread.Sleep(400);
        ExpandSchemaPath(main, SchemaExpandTokens);

        if (!string.IsNullOrEmpty(intermediateShotDir))
        {
            SaveShot(main, Path.Combine(intermediateShotDir, "06c-schema-expanded.png"));
        }

        // Advanced SQL in Document editor + run (never paste while Import/History focused).
        FocusSqlDocument(main, automation);
        Thread.Sleep(300);
        TrySelectConnection(main, ["NZ", "JUST", "NETEZZA"]);
        FocusSqlDocument(main, automation);
        PasteTextIntoSqlEditor(main, automation, AdvancedEvo1Sql);
        Thread.Sleep(400);

        if (!string.IsNullOrEmpty(intermediateShotDir))
        {
            SaveShot(main, Path.Combine(intermediateShotDir, "06d-sql-typed.png"));
        }

        EnsureJustyBaseForeground(main);
        var run = FindByNameOrId(main, automation, "RunSql");
        if (!TryClick(run))
        {
            EnsureJustyBaseForeground(main);
            Keyboard.Press(VirtualKeyShort.F5);
        }

        var gotResults = WaitForSqlResults(main, automation, TimeSpan.FromSeconds(90));
        DismissMessageDialogs(automation);
        Thread.Sleep(800);

        // Best-effort: reopen Schema Search filter for the shot (COM-safe).
        try
        {
            _ = TryClick(FindByNameContains(main, "Schema search"))
                || TryClick(FindByNameContains(main, "Schema Search"));
            Thread.Sleep(200);
            _ = TryClick(FindByNameOrId(main, automation, "SchemaSearchShowFilter"));
        }
        catch
        {
        }

        FocusSqlDocument(main, automation);
        TryRestoreAndFocus(main);
        Thread.Sleep(300);

        return gotResults && !HasVisibleErrorDialog(automation);
    }

    /// <summary>
    /// Prefer UIA ValuePattern (no clipboard — Keep/other apps often steal focus on clipboard change).
    /// Falls back to WM_CHAR posted to the control HWND (still no clipboard / no global Ctrl+V).
    /// </summary>
    public static void SetControlText(Window main, AutomationElement el, string text)
    {
        EnsureJustyBaseForeground(main);
        TryClick(el);
        Thread.Sleep(80);
        try
        {
            if (el.Patterns.Value.IsSupported)
            {
                el.Patterns.Value.Pattern.SetValue(text);
                return;
            }
        }
        catch
        {
        }

        EnterTextWithoutClipboard(main, el, text);
    }

    public static void PasteTextIntoSqlEditor(Window main, UIA3Automation automation, string text)
    {
        FocusSqlDocument(main, automation);
        var editor = FindByNameOrId(main, automation, "SqlEditor");
        if (editor is not null)
        {
            TryClick(editor);
            Thread.Sleep(100);
            EnterTextWithoutClipboard(main, editor, text);
            return;
        }

        EnterTextWithoutClipboard(main, main, text);
    }

    /// <summary>
    /// Inject text without clipboard and without Ctrl+V (clipboard changes can activate Google Keep).
    /// Uses ValuePattern when possible; otherwise WM_CHAR to the target HWND belonging to Justy Base.
    /// </summary>
    public static void EnterTextWithoutClipboard(Window main, AutomationElement target, string text)
    {
        EnsureJustyBaseForeground(main);

        try
        {
            if (target.Patterns.Value.IsSupported)
            {
                target.Patterns.Value.Pattern.SetValue(text);
                return;
            }
        }
        catch
        {
        }

        var hwnd = GetNativeHwnd(target);
        if (hwnd == IntPtr.Zero)
        {
            hwnd = GetNativeHwnd(main);
        }

        _ = GetWindowThreadProcessId(GetNativeHwnd(main), out uint ourPid);
        _ = GetWindowThreadProcessId(hwnd, out uint targetPid);
        if (ourPid == 0 || targetPid != ourPid)
        {
            throw new InvalidOperationException(
                "Refusing text injection: target HWND is not owned by Justy Base process.");
        }

        // Select-all via messages to our HWND only (not global keyboard).
        PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VirtualKeyShort.CONTROL, IntPtr.Zero);
        PostMessage(hwnd, WM_KEYDOWN, (IntPtr)0x41 /* A */, IntPtr.Zero);
        PostMessage(hwnd, WM_KEYUP, (IntPtr)0x41, IntPtr.Zero);
        PostMessage(hwnd, WM_KEYUP, (IntPtr)VirtualKeyShort.CONTROL, IntPtr.Zero);
        Thread.Sleep(30);

        foreach (var ch in text)
        {
            EnsureJustyBaseForeground(main);
            PostMessage(hwnd, WM_CHAR, (IntPtr)ch, IntPtr.Zero);
        }
    }

    [Obsolete("Clipboard paste is forbidden — activates other apps (e.g. Keep). Use EnterTextWithoutClipboard / SetControlText.")]
    public static void PasteText(Window main, string text)
        => EnterTextWithoutClipboard(main, main, text);

    [Obsolete("Use PasteTextIntoSqlEditor / SetControlText — no clipboard.")]
    public static void PasteText(string text)
        => throw new InvalidOperationException("Clipboard paste disabled. Use PasteTextIntoSqlEditor / SetControlText.");

    public static void TrySelectConnection(Window main, string[] preferredNameFragments)
    {
        EnsureJustyBaseForeground(main);
        foreach (var fragment in preferredNameFragments)
        {
            // Prefer combos near the SQL toolbar (avoid Schema Search connection ComboBox).
            var combos = Flatten(main)
                .Where(e => e.ControlType == ControlType.ComboBox)
                .Take(6)
                .ToArray();
            foreach (var combo in combos)
            {
                EnsureJustyBaseForeground(main);
                TryClick(combo);
                Thread.Sleep(200);
                var choice = FindByNameContains(main, fragment);
                if (choice is not null && TryClick(choice))
                {
                    Thread.Sleep(300);
                    EnsureJustyBaseForeground(main);
                    return;
                }

                EnsureJustyBaseForeground(main);
                Keyboard.Press(VirtualKeyShort.ESCAPE);
            }
        }
    }

    public static void DismissMessageDialogs(UIA3Automation automation)
    {
        try
        {
            foreach (var win in automation.GetDesktop().FindAllChildren(cf => cf.ByControlType(ControlType.Window)))
            {
                var title = win.Name ?? "";
                if (title.Contains("Message", StringComparison.OrdinalIgnoreCase)
                    || title.Contains("Error", StringComparison.OrdinalIgnoreCase))
                {
                    win.SetForeground();
                    Keyboard.Press(VirtualKeyShort.ESCAPE);
                    Thread.Sleep(150);
                    Keyboard.Press(VirtualKeyShort.RETURN);
                    Thread.Sleep(150);
                }
            }
        }
        catch
        {
        }
    }

    public static bool HasVisibleErrorDialog(UIA3Automation automation)
    {
        try
        {
            return automation.GetDesktop().FindAllChildren(cf => cf.ByControlType(ControlType.Window))
                .Any(w =>
                {
                    var n = w.Name ?? "";
                    return n.Contains("Message", StringComparison.OrdinalIgnoreCase)
                           || n.Contains("Error", StringComparison.OrdinalIgnoreCase);
                });
        }
        catch
        {
            return false;
        }
    }

    public static void FocusSqlDocument(Window main, UIA3Automation automation)
    {
        EnsureJustyBaseForeground(main);
        var editor = FindByNameOrId(main, automation, "SqlEditor");
        if (TryClick(editor))
        {
            EnsureJustyBaseForeground(main);
            return;
        }

        _ = TryClick(FindByNameContains(main, "Document1"))
            || TryClick(FindByNameContains(main, "Document"))
            || TryClick(FindByNameContains(main, "SqlDocument"));
        Thread.Sleep(200);
        EnsureJustyBaseForeground(main);
        // No coordinate fallback — blind clicks can land on Keep / desktop behind the window.
    }

    public static void ExpandSchemaPath(Window main, IReadOnlyList<string>? tokens = null)
    {
        EnsureJustyBaseForeground(main);
        foreach (var token in tokens ?? SchemaExpandTokens)
        {
            var node = FindByNameContains(main, token);
            if (node is null)
            {
                continue;
            }

            try
            {
                EnsureJustyBaseForeground(main);
                node.Focus();
                node.DoubleClick();
            }
            catch
            {
                EnsureJustyBaseForeground(main);
                TryClick(node);
                EnsureJustyBaseForeground(main);
                Keyboard.Press(VirtualKeyShort.RIGHT);
            }

            Thread.Sleep(500);
        }

        var dim = FindByNameContains(main, "DIMDATE")
                  ?? FindByNameContains(main, "DIM_DATE");
        if (dim is not null)
        {
            EnsureJustyBaseForeground(main);
            TryClick(dim);
            Thread.Sleep(300);
        }
    }

    public static void WaitUntilSchemaSearchIdle(Window main, UIA3Automation automation, TimeSpan timeout)
    {
        var refresh = FindByNameOrId(main, automation, "SchemaSearchRefresh");
        var started = DateTime.UtcNow;
        Thread.Sleep(800);
        while (DateTime.UtcNow - started < timeout)
        {
            try
            {
                if (refresh is null || refresh.IsEnabled)
                {
                    return;
                }
            }
            catch
            {
                refresh = FindByNameOrId(main, automation, "SchemaSearchRefresh");
            }

            Thread.Sleep(400);
        }
    }

    public static bool WaitForSqlResults(Window main, UIA3Automation automation, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            if (HasVisibleErrorDialog(automation))
            {
                return false;
            }

            try
            {
                var resultsRoot = FindByNameOrId(main, automation, "SqlResults")
                                  ?? FindByNameOrId(main, automation, "ResultDataGrid")
                                  ?? FindByNameContains(main, "Results");
                if (resultsRoot is not null)
                {
                    var grid = Flatten(resultsRoot)
                        .FirstOrDefault(c =>
                            c.ControlType is ControlType.DataGrid or ControlType.Table);
                    if (grid is not null && grid.FindAllChildren().Length > 0)
                    {
                        return true;
                    }

                    if (Flatten(resultsRoot).Any(c => c.ControlType == ControlType.DataItem))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            Thread.Sleep(500);
        }

        return false;
    }

    private static bool TryCreateNewDocumentTab(Window main, UIA3Automation automation)
    {
        // Dock "new document" plus button only — never fall back to absolute screen clicks.
        var plus = FindByNameOrId(main, automation, "Create Document")
                   ?? FindByNameOrId(main, automation, "CreateDocument")
                   ?? Flatten(main).FirstOrDefault(e =>
                       e.ControlType == ControlType.Button
                       && (e.Name == "+" || e.Name.Contains("New Document", StringComparison.OrdinalIgnoreCase)));

        return TryClick(plus);
    }

    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "JustyBase.slnx"))
                && Directory.Exists(Path.Combine(dir.FullName, "pictures")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    public static string ResolveJustyBaseExe(string repoRoot)
    {
        string? fromEnv = Environment.GetEnvironmentVariable("JUSTYBASE_EXE");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return fromEnv;
        }

        string[] candidates =
        [
            Path.Combine(repoRoot, "source", "JustyBase", "bin", "Debug", "net10.0", "JustyBase.exe"),
            Path.Combine(repoRoot, "source", "JustyBase", "bin", "Release", "net10.0", "JustyBase.exe"),
            Path.Combine(repoRoot, "source", "JustyBase", "bin", "Debug", "net10.0", "win-x64", "JustyBase.exe"),
            Path.Combine(repoRoot, "source", "JustyBase", "bin", "Release", "net10.0", "win-x64", "JustyBase.exe"),
        ];

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    public static FlaUIApplication? TryAttachExisting(UIA3Automation automation)
    {
        foreach (var p in Process.GetProcessesByName("JustyBase"))
        {
            try
            {
                var app = FlaUIApplication.Attach(p.Id);
                var win = app.GetMainWindow(automation, TimeSpan.FromSeconds(2));
                if (win is not null && win.Title.Contains("Justy", StringComparison.OrdinalIgnoreCase))
                {
                    return app;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    public static FlaUIApplication LaunchOrAttach(UIA3Automation automation, string exePath)
    {
        var existing = TryAttachExisting(automation);
        if (existing is not null)
        {
            return existing;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = true
        };
        return FlaUIApplication.Launch(startInfo);
    }

    public static Window WaitForMainWindow(FlaUIApplication app, UIA3Automation automation)
    {
        // Prefer GetMainWindow — GetAllTopLevelWindows can COM-timeout under load.
        var result = Retry.WhileNull(
            () =>
            {
                try
                {
                    var main = app.GetMainWindow(automation, TimeSpan.FromSeconds(3));
                    if (main is not null
                        && !string.IsNullOrWhiteSpace(main.Title)
                        && main.Title.Contains("Justy", StringComparison.OrdinalIgnoreCase)
                        && !main.Title.Contains("Splash", StringComparison.OrdinalIgnoreCase))
                    {
                        return main;
                    }
                }
                catch
                {
                }

                try
                {
                    return app.GetAllTopLevelWindows(automation)
                        .FirstOrDefault(w =>
                            !string.IsNullOrWhiteSpace(w.Title)
                            && w.Title.Contains("Justy", StringComparison.OrdinalIgnoreCase)
                            && !w.Title.Contains("Splash", StringComparison.OrdinalIgnoreCase));
                }
                catch
                {
                    return null;
                }
            },
            AppStartTimeout,
            interval: TimeSpan.FromMilliseconds(500),
            throwOnTimeout: false,
            ignoreException: true);

        Assert.True(result.Success && result.Result is not null, "Timed out waiting for Justy Base main window.");
        return result.Result!;
    }

    public static AutomationElement? FindByNameOrId(AutomationElement root, UIA3Automation automation, string nameOrId)
    {
        ConditionFactory cf = new(automation.PropertyLibrary);
        return root.FindFirstDescendant(cf.ByAutomationId(nameOrId))
               ?? root.FindFirstDescendant(cf.ByName(nameOrId))
               ?? FindByNameContains(root, nameOrId);
    }

    public static AutomationElement? FindByNameContains(AutomationElement root, string fragment)
    {
        foreach (var e in Flatten(root))
        {
            string? name = null;
            try
            {
                name = e.Name;
            }
            catch
            {
                continue;
            }

            if (!string.IsNullOrEmpty(name)
                && name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return e;
            }
        }

        return null;
    }

    public static IEnumerable<AutomationElement> Flatten(AutomationElement root)
    {
        var stack = new Stack<AutomationElement>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;
            AutomationElement[] children;
            try
            {
                children = current.FindAllChildren();
            }
            catch
            {
                continue;
            }

            for (int i = children.Length - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }
    }

    public static bool TryClick(AutomationElement? el)
    {
        if (el is null)
        {
            return false;
        }

        try
        {
            el.Focus();
            el.Click();
            return true;
        }
        catch
        {
            try
            {
                el.AsButton()?.Invoke();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void TryClickByName(Window main, UIA3Automation automation, string name)
    {
        var el = FindByNameOrId(main, automation, name)
                 ?? FindByNameOrId(main, automation, "_" + name);
        _ = TryClick(el);
    }

    public static void SaveShot(AutomationElement element, params string[] paths)
    {
        using var bitmap = CaptureWindowOnly(element)
            ?? throw new InvalidOperationException("Failed to capture application window bitmap.");

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    /// <summary>
    /// Captures <b>only</b> the target HWND (no desktop wallpaper / other apps).
    /// Prefer <c>PrintWindow</c> (window's own pixels). Screen BitBlt of DWM bounds is a
    /// last resort because Win11 drop-shadow regions composite the desktop behind the window.
    /// </summary>
    private static System.Drawing.Bitmap? CaptureWindowOnly(AutomationElement element)
    {
        var hwnd = GetNativeHwnd(element);
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        if (!TryGetVisibleWindowBounds(hwnd, out var bounds) || bounds.Width < 2 || bounds.Height < 2)
        {
            return null;
        }

        // 1) PrintWindow — no desktop bleed through shadow.
        var printed = CaptureViaPrintWindow(hwnd, bounds.Width, bounds.Height);
        if (printed is not null)
        {
            using (printed)
            {
                if (!IsMostlyEmpty(printed))
                {
                    return CropNonEmptyContent(printed) ?? (System.Drawing.Bitmap)printed.Clone();
                }
            }
        }

        // 2) Fallback: BitBlt visible frame, inset to drop soft shadow that shows wallpaper.
        try
        {
            var inset = InsetRectangle(bounds, shadowInsetPx: EstimateShadowInset(bounds));
            using var screen = Capture.Rectangle(inset);
            using var raw = (System.Drawing.Bitmap)screen.Bitmap.Clone();
            return CropNonEmptyContent(raw) ?? (System.Drawing.Bitmap)raw.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static System.Drawing.Rectangle InsetRectangle(System.Drawing.Rectangle bounds, int shadowInsetPx)
    {
        int inset = Math.Max(0, shadowInsetPx);
        int w = Math.Max(1, bounds.Width - inset * 2);
        int h = Math.Max(1, bounds.Height - inset * 2);
        return new System.Drawing.Rectangle(bounds.X + inset, bounds.Y + inset, w, h);
    }

    private static int EstimateShadowInset(System.Drawing.Rectangle bounds)
    {
        // Win11 DWM soft shadow is typically ~8–16 physical px; scale mildly with window size.
        return Math.Clamp(Math.Min(bounds.Width, bounds.Height) / 80, 8, 16);
    }

    private static bool IsMostlyEmpty(System.Drawing.Bitmap bitmap)
    {
        try
        {
            int empty = 0;
            int samples = 0;
            for (int y = 4; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 10))
            {
                for (int x = 4; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 10))
                {
                    samples++;
                    if (IsEmptyCapturePixel(bitmap.GetPixel(x, y)))
                    {
                        empty++;
                    }
                }
            }

            return samples > 0 && empty * 100 / samples > 85;
        }
        catch
        {
            return false;
        }
    }

    private static System.Drawing.Bitmap? CaptureViaPrintWindow(IntPtr hwnd, int width, int height)
    {
        System.Drawing.Bitmap? bitmap = null;
        try
        {
            bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.Clear(System.Drawing.Color.Magenta);
            var hdc = graphics.GetHdc();
            try
            {
                if (!PrintWindow(hwnd, hdc, 2))
                {
                    bitmap.Dispose();
                    return null;
                }
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }

            var result = bitmap;
            bitmap = null; // ownership transferred to caller
            return result;
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    private static IntPtr GetNativeHwnd(AutomationElement element)
    {
        try
        {
            if (element.Properties.NativeWindowHandle.IsSupported)
            {
                var hwnd = element.Properties.NativeWindowHandle.Value;
                if (hwnd != IntPtr.Zero)
                {
                    return hwnd;
                }
            }
        }
        catch
        {
        }

        try
        {
            var current = element.Parent;
            for (var i = 0; i < 8 && current is not null; i++)
            {
                if (current.Properties.NativeWindowHandle.IsSupported)
                {
                    var hwnd = current.Properties.NativeWindowHandle.Value;
                    if (hwnd != IntPtr.Zero)
                    {
                        return hwnd;
                    }
                }

                current = current.Parent;
            }
        }
        catch
        {
        }

        return IntPtr.Zero;
    }

    private static bool TryGetVisibleWindowBounds(IntPtr hwnd, out System.Drawing.Rectangle bounds)
    {
        bounds = default;
        // Prefer DWM visible frame — excludes invisible Win10 resize borders that otherwise
        // pull desktop wallpaper into the capture.
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var dwmRect, System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) == 0)
        {
            bounds = ToRectangle(dwmRect);
            if (bounds.Width > 1 && bounds.Height > 1)
            {
                return true;
            }
        }

        if (!GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        bounds = ToRectangle(rect);
        return bounds.Width > 1 && bounds.Height > 1;
    }

    private static System.Drawing.Rectangle ToRectangle(RECT rect)
        => new(
            rect.Left,
            rect.Top,
            Math.Max(1, rect.Right - rect.Left),
            Math.Max(1, rect.Bottom - rect.Top));

    /// <summary>
    /// Trims only fully-empty margin rows/cols (DPI under-draw / Magenta sentinel).
    /// Does not shrink based on interior white UI pixels.
    /// </summary>
    private static System.Drawing.Bitmap? CropNonEmptyContent(System.Drawing.Bitmap source)
    {
        try
        {
            int top = 0, bottom = source.Height - 1, left = 0, right = source.Width - 1;

            while (top <= bottom && IsEmptyRow(source, top)) top++;
            while (bottom >= top && IsEmptyRow(source, bottom)) bottom--;
            while (left <= right && IsEmptyColumn(source, left, top, bottom)) left++;
            while (right >= left && IsEmptyColumn(source, right, top, bottom)) right--;

            if (bottom < top || right < left)
            {
                return null;
            }

            if (top == 0 && left == 0 && right == source.Width - 1 && bottom == source.Height - 1)
            {
                return (System.Drawing.Bitmap)source.Clone();
            }

            var rect = new System.Drawing.Rectangle(left, top, right - left + 1, bottom - top + 1);
            return source.Clone(rect, source.PixelFormat);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsEmptyRow(System.Drawing.Bitmap source, int y)
    {
        for (int x = 0; x < source.Width; x++)
        {
            if (!IsEmptyCapturePixel(source.GetPixel(x, y)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsEmptyColumn(System.Drawing.Bitmap source, int x, int y0, int y1)
    {
        for (int y = y0; y <= y1; y++)
        {
            if (!IsEmptyCapturePixel(source.GetPixel(x, y)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsEmptyCapturePixel(System.Drawing.Color c)
    {
        if (c.A < 8)
        {
            return true;
        }

        // Magenta sentinel from PrintWindow path
        if (c.R > 250 && c.G < 5 && c.B > 250)
        {
            return true;
        }

        // Uniform padding only (entire row/col must match) — near-white DPI under-draw
        if (c.R > 250 && c.G > 250 && c.B > 250)
        {
            return true;
        }

        return false;
    }

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int SW_RESTORE = 9;
    private const uint GA_ROOT = 2;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_CHAR = 0x0102;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, int nFlags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static void DumpUiTree(AutomationElement root, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# UIA tree dump — {DateTime.UtcNow:O}");
        sb.AppendLine($"# Root: '{root.Name}'");
        sb.AppendLine();
        int nodes = 0;
        foreach (var el in Flatten(root))
        {
            if (++nodes > 900)
            {
                sb.AppendLine("… truncated …");
                break;
            }

            string name = el.Name ?? "";
            string type = el.Properties.ControlType.IsSupported
                ? el.Properties.ControlType.Value.ToString()
                : "?";
            sb.AppendLine($"- [{type}] name='{name}'");
        }

        File.WriteAllText(path, sb.ToString());
    }
}
