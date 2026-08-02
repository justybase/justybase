using System.Diagnostics;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using FlaUIApplication = FlaUI.Core.Application;

namespace JustyBase.LiveTests;

/// <summary>
/// README hero: Schema Search refresh → expand schema to JUST_DATA / DIMDATE → run SELECT → screenshot results.
/// Requires a live connection that can resolve JUST_DATA..DIMDATE (typically NZ).
/// Run: dotnet test source/JustyBase.LiveTests --filter FullyQualifiedName~ReadmeSqlResultsHero
/// </summary>
[Trait("Category", "Live")]
public sealed class FlaUIReadmeSqlResultsHeroTests
{
    private const string SqlQuery = "SELECT * FROM JUST_DATA..DIMDATE";

    [Fact]
    public void Capture_SchemaRefresh_ExpandDimDate_RunSelect_ScreenshotForReadme()
    {
        var repoRoot = FlaUILiveHarness.FindRepoRoot();
        var exePath = FlaUILiveHarness.ResolveJustyBaseExe(repoRoot);
        Assert.True(File.Exists(exePath), $"JustyBase.exe not found. Build first. Looked at: {exePath}");

        var picturesDir = Path.Combine(repoRoot, "pictures");
        var liveDir = Path.Combine(picturesDir, "live");
        Directory.CreateDirectory(liveDir);

        Process? launched = null;
        using var automation = new UIA3Automation();
        FlaUIApplication? app = null;

        try
        {
            app = FlaUILiveHarness.LaunchOrAttach(automation, exePath);
            if (app.ProcessId > 0)
            {
                try { launched = Process.GetProcessById(app.ProcessId); } catch { /* ignore */ }
            }

            var main = FlaUILiveHarness.WaitForMainWindow(app, automation);
            FlaUILiveHarness.MinimizeAllWindowsThenFocus(main);
            Thread.Sleep(FlaUILiveHarness.Settle);
            FlaUILiveHarness.DismissMessageDialogs(automation);

            // 1) Focus Schema Search tool + refresh metadata.
            _ = FlaUILiveHarness.TryClick(FlaUILiveHarness.FindByNameContains(main, "Schema search"))
                || FlaUILiveHarness.TryClick(FlaUILiveHarness.FindByNameContains(main, "Schema Search"));
            Thread.Sleep(400);

            var refresh = FlaUILiveHarness.FindByNameOrId(main, automation, "SchemaSearchRefresh")
                          ?? FlaUILiveHarness.FindByNameContains(main, "Refresh");
            Assert.True(FlaUILiveHarness.TryClick(refresh), "Schema Search Refresh control not found.");
            FlaUILiveHarness.WaitUntilSchemaSearchIdle(main, automation, TimeSpan.FromSeconds(90));
            FlaUILiveHarness.DismissMessageDialogs(automation);
            FlaUILiveHarness.SaveShot(main, Path.Combine(liveDir, "10-schema-search-refreshed.png"));

            var searchBox = FlaUILiveHarness.FindByNameContains(main, "Search...")
                            ?? main.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Edit));
            if (searchBox is not null)
            {
                FlaUILiveHarness.TryClick(searchBox);
                FlaUILiveHarness.SetControlText(main, searchBox, "DIMDATE");
                Thread.Sleep(800);
                FlaUILiveHarness.SaveShot(main, Path.Combine(liveDir, "11-schema-search-dimdate.png"));
            }

            // 2) Expand Schema tree toward JUST_DATA / DIMDATE.
            _ = FlaUILiveHarness.TryClick(FlaUILiveHarness.FindByNameContains(main, "Schema"))
                || FlaUILiveHarness.TryClick(FlaUILiveHarness.FindByNameOrId(main, automation, "DbSchemaTree"));
            Thread.Sleep(400);

            FlaUILiveHarness.ExpandSchemaPath(main);
            FlaUILiveHarness.SaveShot(main, Path.Combine(liveDir, "12-schema-tree-dimdate.png"));

            // 3) SQL tab: clipboard-paste query (avoids mangling "..") and run.
            FlaUILiveHarness.FocusSqlDocument(main, automation);
            Thread.Sleep(300);
            FlaUILiveHarness.TrySelectConnection(main, ["NZ", "JUST", "NETEZZA"]);
            FlaUILiveHarness.PasteTextIntoSqlEditor(main, automation, SqlQuery);
            Thread.Sleep(400);
            FlaUILiveHarness.SaveShot(main, Path.Combine(liveDir, "12b-sql-typed.png"));

            var run = FlaUILiveHarness.FindByNameOrId(main, automation, "RunSql");
            if (!FlaUILiveHarness.TryClick(run))
            {
                FlaUILiveHarness.EnsureJustyBaseForeground(main);
                Keyboard.Press(VirtualKeyShort.F5);
            }

            var gotResults = FlaUILiveHarness.WaitForSqlResults(main, automation, TimeSpan.FromSeconds(90));
            FlaUILiveHarness.DismissMessageDialogs(automation);
            Thread.Sleep(800);
            FlaUILiveHarness.TryRestoreAndFocus(main);
            Thread.Sleep(200);

            FlaUILiveHarness.SaveShot(main, Path.Combine(liveDir, "13-sql-results-hero.png"));
            FlaUILiveHarness.DumpUiTree(main, Path.Combine(liveDir, "uia-tree-hero.txt"));
            Assert.True(File.Exists(Path.Combine(liveDir, "13-sql-results-hero.png")));

            // Only overwrite README main hero when we actually got a result grid (not a connect error).
            // evo1.png is owned by the gallery rich scene — do not overwrite it here.
            if (gotResults && !FlaUILiveHarness.HasVisibleErrorDialog(automation))
            {
                FlaUILiveHarness.SaveShot(main, Path.Combine(picturesDir, "main.png"));
            }
            else
            {
                Assert.Fail(
                    "SQL results hero needs a working DB connection for JUST_DATA..DIMDATE. "
                    + "Intermediate shots are in pictures/live/10-13*.png. "
                    + "Select NZ connection in the app, then re-run this test.");
            }
        }
        finally
        {
            try { app?.Close(); } catch { /* ignore */ }
            try
            {
                if (launched is { HasExited: false })
                {
                    launched.Kill(entireProcessTree: true);
                }
            }
            catch { /* ignore */ }
        }
    }
}
