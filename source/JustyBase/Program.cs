using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Velopack;

namespace JustyBase;

internal class Program
{
    private static readonly IProgramErrorHandlingService DefaultProgramErrorHandlingService = new ProgramErrorHandlingService();

    // Kept for process lifetime so the kernel object is not released early (GC).
    private static SingleInstanceStartupCoordinator? _singleInstanceCoordinator;

    private const string SingleInstanceMutexName = @"Local\JustyBase_SingleInstance_JUST_X";

    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack clears this marker in Run(), so capture it before the first
        // Velopack call. The restarted process may briefly overlap the old one.
        bool startedByVelopackRestart = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("VELOPACK_RESTART"));
        StartupTrace.Write($"start velopackRestart={startedByVelopackRestart}");

        // Keep the startup fallback enabled. If the updater cannot finish while
        // the app is shutting down (for example because another process still
        // holds a file), Velopack can retry the downloaded package on the next
        // launch instead of leaving it pending forever.
        VelopackApp.Build().Run();
        var provider = CodePagesEncodingProvider.Instance;
        Encoding.RegisterProvider(provider);
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        // Mutex is reliable for single-instance; File.Exists(pipe) races during startup
        // and while the server recreates the pipe after each client.
        _singleInstanceCoordinator = new SingleInstanceStartupCoordinator(
            SingleInstanceMutexName,
            startedByVelopackRestart);
        StartupTrace.Write($"mutex primary={_singleInstanceCoordinator.IsPrimary} restartWaitTimedOut={_singleInstanceCoordinator.IsRestartWaitTimedOut}");

        if (!_singleInstanceCoordinator.IsPrimary)
        {
            TryNotifyRunningInstance(args);
            return;
        }

        // Kill llama-server children orphaned by a previously crashed or force-killed
        // instance (they hold VRAM/RAM and are never auto-exited). Only servers whose
        // owner process is dead are touched — a running second instance is safe.
        var orphanedServers = JustyBase.Ai.Embedded.Server.LlamaServerProcessRegistry.CleanupOrphans();
        if (orphanedServers > 0)
        {
            Debug.WriteLine($"Cleaned up {orphanedServers} orphaned llama-server process(es).");
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception globalException)
        {
            GetProgramErrorHandlingService.HandleStartupException(globalException, GetSimpleLogger, GetMessagesService);
        }
    }

    /// <summary>
    /// Forwards open-file / restore to the already running UI instance.
    /// Retries because the primary process may still be starting the pipe server.
    /// </summary>
    private static void TryNotifyRunningInstance(string[] args)
    {
        const int maxAttempts = 50;
        const int retryDelayMs = 100;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                using NamedPipeClientStream client = new(".", JbMessagePipeName, PipeDirection.InOut);
                client.Connect(timeout: 200);
                using StreamWriter streamWriter = new(client) { AutoFlush = true };

                // try to open next sql file from system (not JB inner option)
                if (args.Length >= 1 && args[^1].EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                {
                    streamWriter.WriteLine(args[^1]);
                }

                streamWriter.WriteLine("RESTORE");
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TryNotifyRunningInstance attempt {attempt + 1}: {ex.Message}");
                Thread.Sleep(retryDelayMs);
            }
        }

        Debug.WriteLine("TryNotifyRunningInstance: gave up waiting for running instance pipe.");
    }


    private static IServiceProvider? _serviceProvider;
    public static IServiceProvider? ServiceProvider => _serviceProvider;
    private static IGeneralApplicationData? GetGeneralApplicationData => _serviceProvider?.GetRequiredService<IGeneralApplicationData>();
    private static ISimpleLogger? GetSimpleLogger => _serviceProvider?.GetRequiredService<ISimpleLogger>();
    private static IMessageForUserTools? GetMessagesService => _serviceProvider?.GetRequiredService<IMessageForUserTools>();
    private static IProgramErrorHandlingService GetProgramErrorHandlingService => _serviceProvider?.GetRequiredService<IProgramErrorHandlingService>() ?? DefaultProgramErrorHandlingService;

    public static void SetServiceProvider(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;


    public static void SetUpDispatcherExceptionHandling()
    {
        Dispatcher.UIThread.UnhandledException += UIThread_UnhandledException;
        Dispatcher.UIThread.UnhandledExceptionFilter += UIThread_UnhandledExceptionFilter;
    }

    private static void UIThread_UnhandledExceptionFilter(object sender, DispatcherUnhandledExceptionFilterEventArgs e)
    {
        GetProgramErrorHandlingService.HandleUiThreadException(e.Exception, GetSimpleLogger, GetMessagesService, "UIThread_UnhandledExceptionFilter");
    }

    private static void UIThread_UnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        GetProgramErrorHandlingService.HandleUiThreadException(e.Exception, GetSimpleLogger, GetMessagesService, "UIThread_UnhandledException");
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        GetProgramErrorHandlingService.HandleCurrentDomainUnhandledException(e.ExceptionObject, e.ToString() ?? string.Empty, GetSimpleLogger);
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        //to actually observe the task, uncomment the below line of code
        e.SetObserved();
        Debug.WriteLine("TaskScheduler_UnobservedTaskException");

        GetProgramErrorHandlingService.HandleUnobservedTaskException(
            e.Exception,
            GetGeneralApplicationData,
            GetSimpleLogger,
            GetMessagesService);
    }
    public const string JbMessagePipeName = @"JUST_X";

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
#if DEBUG
        .WithDeveloperTools()
#endif
        //.UseReactiveUI()
        //.WithInterFont()
        .LogToTrace();
}
