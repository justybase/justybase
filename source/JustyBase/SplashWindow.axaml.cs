using Microsoft.Win32;
using System.Runtime.InteropServices;
using JustyBase.PluginCommon.Contracts;
#if LOTTIE
using Avalonia.Labs.Lottie;
#endif

namespace JustyBase;

public partial class SplashWindow : Window
{
    private readonly Action? _mainAction;
    private readonly ISimpleLogger _simpleLogger;
#if LOTTIE
    private Lottie? _lottie;
#endif

    public SplashWindow() : this(null, null!)
    {
    }

    public SplashWindow(Action? mainAction, ISimpleLogger simpleLogger)
    {
        StartupTrace.Write("SplashWindow constructor start");
        InitializeComponent();
        _mainAction = mainAction;
        _simpleLogger = simpleLogger;
        SetLottie();
        Closed += (_, _) => StartupTrace.Write("SplashWindow Closed");
        StartupTrace.Write($"SplashWindow constructor completed waitMs={_timeToWait}");
    }

    public static bool IsValentine()
    {
        return DateTime.Now.Month == 2 && DateTime.Now.Day == 14;
    }
#if LOTTIE
    private static readonly List<(int timeInMs, string assetPath)> StartupOptions =
        [
            (1_000, @"avares://JustyBase/Assets/gears.json"),
            (3_000, @"avares://JustyBase/Assets/frog.json"),
            (1_650, @"avares://JustyBase/Assets/loading.json"),
            (2_300, @"avares://JustyBase/Assets/welcome.json")
        ];
#endif

    private void SetLottie()
    {
#if LOTTIE
        _lottie = new Lottie(new Uri("avares://JustyBase/SplashWindow.axaml"))
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            RepeatCount = -1
        };
        LottieHost.Content = _lottie;
        StaticSplash.IsVisible = false;
        if (IsValentine())
        {
            //SpecialWasShown = true;
            Background = Brushes.Transparent;
            WindowDecorations = WindowDecorations.None;
            ExtendClientAreaToDecorationsHint = false;
            Width *= 2.0;
            Height *= 2.0;
            _timeToWait = 4_000;
            _lottie.RepeatCount = 2;
            _lottie.Path = @"avares://JustyBase/Assets/Hearth1.json";
        }
        else
        {
#pragma warning disable CA5394 // Splash content selection is not security-sensitive.
            var nm = Random.Shared.Next(0, StartupOptions.Count);
#pragma warning restore CA5394
            _timeToWait = StartupOptions[nm].timeInMs;
            _lottie.Path = StartupOptions[nm].assetPath;
        }
#else
        // Slim build without Lottie: static logo instead of the animation.
        LottieHost.IsVisible = false;
        StaticSplash.IsVisible = true;
        _timeToWait = 1_500;
#endif
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        StartupTrace.Write("SplashWindow OnLoaded start");
        await DummyLoad();
        StartupTrace.Write("SplashWindow OnLoaded completed");
    }
    private int _timeToWait = 1_500;
    private async Task DummyLoad()
    {
        StartupTrace.Write("SplashWindow DummyLoad start");
        if (OperatingSystem.IsWindows())
        {
            StartupTrace.Write("SplashWindow before SetFileTypeAssociation");
            SetFileTypeAssociation();
            StartupTrace.Write("SplashWindow after SetFileTypeAssociation");
        }
        // Do some background stuff here.
        StartupTrace.Write($"SplashWindow delaying ms={_timeToWait}");
        await Task.Delay(_timeToWait);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                StartupTrace.Write("SplashWindow before main callback");
                _mainAction?.Invoke();
                StartupTrace.Write("SplashWindow after main callback; calling Close");
                Close();
                StartupTrace.Write("SplashWindow Close completed");
            }
            catch (Exception ex)
            {
                StartupTrace.WriteException("SplashWindow callback", ex);
                _simpleLogger?.TrackError(ex, isCrash: false);
            }
        });
    }



    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("Shell32.dll")]
    private static partial int SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

    private static void SetFileTypeAssociation()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JustyBase", "current", "JustyBase.exe") + " \"%1\"";
            if ((string)Registry.GetValue("HKEY_CURRENT_USER\\Software\\Classes\\.sql", "", "JustyBase")! == "JustyBase")
                return;

            Registry.SetValue("HKEY_CURRENT_USER\\Software\\Classes\\JustyBase", "", "sql");
            Registry.SetValue("HKEY_CURRENT_USER\\Software\\Classes\\JustyBase", "sql files", "sql");
            Registry.SetValue("HKEY_CURRENT_USER\\Software\\Classes\\JustyBase\\shell\\open\\command", "", path);
            Registry.SetValue("HKEY_CURRENT_USER\\Software\\Classes\\.sql", "", "JustyBase");

            // This call notifies Windows that it needs to redo the file associations and icons
            _ = SHChangeNotify(0x08000000, 0x2000, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            // File association is optional; group policies may deny HKCU writes.
            System.Diagnostics.Debug.WriteLine($"Could not register .sql file association: {ex.Message}");
        }
    }
}
