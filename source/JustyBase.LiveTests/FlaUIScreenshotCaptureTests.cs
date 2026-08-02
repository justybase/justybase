using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using FlaUIApplication = FlaUI.Core.Application;

namespace JustyBase.LiveTests;

/// <summary>
/// Windows-only FlaUI smoke: launch JustyBase.exe, capture README screenshots, dump UIA tree.
/// Run manually: dotnet test source/JustyBase.LiveTests --filter Category=Live
/// </summary>
[Trait("Category", "Live")]
public sealed class FlaUIScreenshotCaptureTests
{
    [Fact]
    public void Capture_MainWindow_And_NavSurfaces_ForReadme()
    {
        var repoRoot = FlaUILiveHarness.FindRepoRoot();
        var exePath = FlaUILiveHarness.ResolveJustyBaseExe(repoRoot);
        Assert.True(File.Exists(exePath), $"JustyBase.exe not found. Build first. Looked at: {exePath}");

        var picturesDir = Path.Combine(repoRoot, "pictures");
        var liveDir = Path.Combine(picturesDir, "live");
        Directory.CreateDirectory(liveDir);

        var treeDumpPath = Path.Combine(liveDir, "uia-tree.txt");
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

            FlaUILiveHarness.SaveShot(main, Path.Combine(picturesDir, "main.png"), Path.Combine(liveDir, "01-main.png"));
            FlaUILiveHarness.DumpUiTree(main, treeDumpPath);

            FlaUILiveHarness.TryClickByName(main, automation, "Settings");
            Thread.Sleep(FlaUILiveHarness.Settle);
            FlaUILiveHarness.SaveShot(main, Path.Combine(liveDir, "02-settings.png"));

            FlaUILiveHarness.TryClickByName(main, automation, "History");
            Thread.Sleep(FlaUILiveHarness.Settle);
            FlaUILiveHarness.SaveShot(main, Path.Combine(liveDir, "03-history.png"));

            FlaUILiveHarness.TryClickByName(main, automation, "Import");
            Thread.Sleep(FlaUILiveHarness.Settle);
            FlaUILiveHarness.SaveShot(main, Path.Combine(liveDir, "04-import.png"));

            FlaUILiveHarness.TryClickByName(main, automation, "About");
            Thread.Sleep(FlaUILiveHarness.Settle);
            var about = WaitForWindowTitle(automation, "About", TimeSpan.FromSeconds(8)) ?? main;
            FlaUILiveHarness.SaveShot(about, Path.Combine(picturesDir, "sample_01.png"), Path.Combine(liveDir, "05-about.png"));
            Keyboard.Press(VirtualKeyShort.ESCAPE);
            Thread.Sleep(400);

            // Leave a SQL Document active — not Import/History — before the rich evo1 scene.
            FlaUILiveHarness.TryClickByName(main, automation, "Document1");
            Thread.Sleep(400);

            // Rich evo1: advanced SQL + results + expanded schema + refreshed Schema Search + column filter.
            var gotResults = FlaUILiveHarness.PrepareRichEvo1Scene(main, automation, liveDir);
            FlaUILiveHarness.TryRestoreAndFocus(main);
            Thread.Sleep(400);
            FlaUILiveHarness.SaveShot(main, Path.Combine(liveDir, "06-layout.png"));

            if (gotResults)
            {
                FlaUILiveHarness.SaveShot(main, Path.Combine(picturesDir, "evo1.png"));
            }
            else
            {
                Assert.Fail(
                    "evo1.png needs a working DB connection (advanced CTE SELECT on JUST_DATA..DIMDATE). "
                    + "Intermediate shots are in pictures/live/06*.png. "
                    + "Select NZ connection in the app, then re-run this test.");
            }

            FlaUILiveHarness.TryRestoreAndFocus(main);
            Thread.Sleep(400);
            FlaUILiveHarness.SaveShot(main, Path.Combine(liveDir, "07-final.png"));

            Assert.True(File.Exists(Path.Combine(liveDir, "01-main.png")));
            Assert.True(new FileInfo(treeDumpPath).Length > 0);
            Assert.True(File.Exists(Path.Combine(picturesDir, "evo1.png")));
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

    private static Window? WaitForWindowTitle(UIA3Automation automation, string titlePart, TimeSpan timeout)
    {
        var result = Retry.WhileNull(
            () => automation.GetDesktop().FindFirstChild(cf =>
                    cf.ByControlType(FlaUI.Core.Definitions.ControlType.Window).And(cf.ByName(titlePart)))
                ?.AsWindow(),
            timeout,
            interval: TimeSpan.FromMilliseconds(250));
        return result.Result;
    }
}
