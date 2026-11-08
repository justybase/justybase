using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using JustyBase.Helpers;
using JustyBase.Services.Screenshot;

namespace JustyBase.Screenshot;

/// <summary>
/// Takes a PNG of the real main window without showing anything on screen: the application starts
/// on Avalonia's headless platform with real Skia drawing, waits for the window to settle (including
/// <see cref="BackgroundWork"/>), captures the compositor's frame and exits.
///
/// Usage:
///   JustyBase.Screenshot --out path.png [--width 1600] [--height 1000] [--wait 500] [script.sql ...]
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!TryParse(args, out string? output, out double? width, out double? height, out int waitMs, out string[] forwarded))
        {
            Console.Error.WriteLine(
                "usage: JustyBase.Screenshot --out <path.png> [--width N] [--height N] [--wait MS] [file.sql ...]");
            return 2;
        }

        string absolute = Path.GetFullPath(output!);
        string? folder = Path.GetDirectoryName(absolute);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        Environment.SetEnvironmentVariable(ScreenshotMode.Variable, absolute);
        if (width is double w)
        {
            Environment.SetEnvironmentVariable(ScreenshotMode.WidthVariable, w.ToString("0"));
        }

        if (height is double h)
        {
            Environment.SetEnvironmentVariable(ScreenshotMode.HeightVariable, h.ToString("0"));
        }

        int exitCode = 1;
        AppBuilder.Configure<JustyBase.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .LogToTrace()
            .AfterSetup(appBuilder =>
            {
                Dispatcher.UIThread.Post(
                    () => { _ = CaptureAsync(absolute, waitMs, code => exitCode = code); },
                    DispatcherPriority.Background);
            })
            .StartWithClassicDesktopLifetime(forwarded);

        return exitCode;
    }

    private static async Task CaptureAsync(string path, int waitMs, Action<int> complete)
    {
        IClassicDesktopStyleApplicationLifetime? lifetime = null;
        try
        {
            lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

            Window? window = null;
            for (int attempt = 0; attempt < 300; attempt++)
            {
                window = lifetime?.MainWindow;
                if (window is { IsVisible: true } && window.Bounds.Width > 1 && window.Bounds.Height > 1)
                {
                    break;
                }

                await Task.Delay(50).ConfigureAwait(true);
            }

            if (window is null)
            {
                Console.Error.WriteLine("screenshot: the application created no visible window.");
                complete(3);
                return;
            }

            // Start-up work (colourising, indexing, metadata) lands through BackgroundWork.
            await BackgroundWork.WhenIdleAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(true);

            if (waitMs > 0)
            {
                await Task.Delay(waitMs).ConfigureAwait(true);
            }

            // One more dispatcher turn lets layout and the render pass finish.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            using var frame = window.CaptureRenderedFrame();
            if (frame is null)
            {
                Console.Error.WriteLine("screenshot: the headless window rendered no frame.");
                complete(3);
                return;
            }

            frame.Save(path);
            Console.WriteLine($"screenshot: {path}");
            complete(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"screenshot: {ex}");
            complete(1);
        }
        finally
        {
            lifetime?.Shutdown();
        }
    }

    private static bool TryParse(
        string[] args,
        out string? output,
        out double? width,
        out double? height,
        out int waitMs,
        out string[] forwarded)
    {
        output = null;
        width = null;
        height = null;
        waitMs = 0;
        var rest = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--width" when i + 1 < args.Length && double.TryParse(args[i + 1], out double parsedWidth):
                    width = parsedWidth;
                    i++;
                    break;
                case "--height" when i + 1 < args.Length && double.TryParse(args[i + 1], out double parsedHeight):
                    height = parsedHeight;
                    i++;
                    break;
                case "--wait" when i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedWait):
                    waitMs = Math.Max(0, parsedWait);
                    i++;
                    break;
                case "--help" or "-h":
                    forwarded = rest.ToArray();
                    return false;
                default:
                    rest.Add(args[i]);
                    break;
            }
        }

        forwarded = rest.ToArray();
        return !string.IsNullOrWhiteSpace(output);
    }
}
