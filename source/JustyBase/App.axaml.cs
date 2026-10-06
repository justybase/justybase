using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using JustyBase.Common.Contracts;
using JustyBase.Editor;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Caching;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Visitor;
using JustyBase.Helpers.Interactions;
using JustyBase.Services;
using JustyBase.Themes;
using JustyBase.ViewModels;
using JustyBase.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace JustyBase;

public class App : Application
{
    private static IThemeManager? _themeManager;
    private static IGeneralApplicationData _generalApplicationData;
    public override void Initialize()
    {
        StartupTrace.Write("App.Initialize start");
        var collection = new ServiceCollection();
        collection.AddCommonServices();
        _services = collection.BuildServiceProvider();
        Program.SetServiceProvider(_services);
        // F1: bridge for manually-constructed models/services (DbSchemaModel children,
        // error-handling static path) so they resolve via DI instead of ServiceLocator.
        // Per-instance ctor params take precedence; this is only a fallback.
        JustyBase.Models.Tools.DbSchemaModel.ResolverProvider =
            () => _services.GetService<JustyBase.Services.Documents.IDatabaseServiceResolver>();
        ProgramErrorHandlingService.ConfigureProvider(
            () => _services.GetService<IGeneralApplicationData>());
        StartupTrace.Write("App.Initialize service provider built");
        // The SQL template is deliberately non-recycling. Dock's document-content cache keeps
        // one editor view per open SQL tab, preserving its visual state across tab switches.
        DataTemplates.Add(new SqlDocumentDataTemplate(_services));
        DataTemplates.Add(new ViewLocator(_services));
        _generalApplicationData = _services.GetRequiredService<IGeneralApplicationData>();
        StartupTrace.Write($"App.Initialize general data loaded theme={_generalApplicationData.Config.ThemeNum} splash={_generalApplicationData.Config.UseSplashScreen}");

        _themeManager = _services.GetRequiredService<IThemeManager>();
        _themeManager.Initialize(this);
        StartupTrace.Write("App.Initialize theme initialized; before AvaloniaXamlLoader.Load");
        AvaloniaXamlLoader.Load(this);
        StartupTrace.Write("App.Initialize AvaloniaXamlLoader.Load completed");

        try
        {
            foreach (var item in IGeneralApplicationData.REGISTERED_EXTENSIONS)
            {
                var (name, assetName, isXml) = item.Value;
                var uri = new Uri($"avares://JustyBase/Assets/{assetName}");
                using (var stream = AssetLoader.Open(uri))
                {
                    using (var reader = new System.Xml.XmlTextReader(stream))
                    {
                        AvaloniaEdit.Highlighting.HighlightingManager.Instance.RegisterHighlighting(item.Value.name, [],
                            AvaloniaEdit.Highlighting.Xshd.HighlightingLoader.Load(reader,
                                AvaloniaEdit.Highlighting.HighlightingManager.Instance));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            StartupTrace.WriteException("syntax highlighting registration", ex);
            Debug.WriteLine($"Failed to register syntax highlighting: {ex.Message}");
        }

        if (_generalApplicationData.Config.ThemeNum == 1)
        {
            SqlCodeEditorHelpers.ResetStyle(dark: true);
            ApplySemanticColors(dark: true);
        }
        else
        {
            ApplySemanticColors(dark: false);
        }

        // Netezza is the default classifier; Db2 documents get a Db2-dialect classifier.
        var schema = _services.GetRequiredService<ISchemaProvider>();
        var coordinator = _services.GetRequiredService<DocumentParsingCoordinator>();
        var netezzaClassifier = _services.GetRequiredService<NzSemanticTokenClassifier>();
        SemanticLineColorizer.Configure(dialect => dialect == SqlDialect.Netezza
            ? netezzaClassifier
            : new NzSemanticTokenClassifier(schema, coordinator, dialect));
        StartupTrace.Write("App.Initialize completed");
    }

    private static void ApplySemanticColors(bool dark)
    {
        if (dark)
        {
            SemanticLineColorizer.SetColors(
                comment: new SolidColorBrush(Colors.Yellow),
                str: new SolidColorBrush(Colors.OrangeRed),
                number: new SolidColorBrush(Colors.Orange),
                keyword: new SolidColorBrush(Colors.LightGreen),
                type: new SolidColorBrush(Colors.BlueViolet),
                function: new SolidColorBrush(Color.FromRgb(250, 0, 250)),
                variable: new SolidColorBrush(Color.FromRgb(0, 200, 200)),
                table: new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                column: new SolidColorBrush(Color.FromRgb(135, 206, 250)),
                cte: new SolidColorBrush(Color.FromRgb(255, 165, 0)),
                alias: new SolidColorBrush(Color.FromRgb(144, 238, 144)),
                identifier: new SolidColorBrush(Color.FromRgb(180, 180, 180)));
        }
        else
        {
            SemanticLineColorizer.SetColors(
                comment: new SolidColorBrush(Colors.Green),
                str: new SolidColorBrush(Colors.Red),
                number: new SolidColorBrush(Colors.Brown),
                keyword: new SolidColorBrush(Colors.Blue),
                type: new SolidColorBrush(Colors.BlueViolet),
                function: new SolidColorBrush(Color.FromRgb(250, 0, 250)),
                variable: new SolidColorBrush(Color.FromRgb(163, 4, 199)),
                table: new SolidColorBrush(Color.FromRgb(160, 82, 45)),
                column: new SolidColorBrush(Color.FromRgb(0, 100, 180)),
                cte: new SolidColorBrush(Color.FromRgb(184, 92, 0)),
                alias: new SolidColorBrush(Color.FromRgb(0, 128, 0)),
                identifier: new SolidColorBrush(Color.FromRgb(120, 120, 120)));
        }
    }

    private static ServiceProvider _services;
    public static T GetRequiredService<T>()
    {
        return _services.GetRequiredService<T>();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        StartupTrace.Write($"App.OnFrameworkInitializationCompleted start lifetime={ApplicationLifetime?.GetType().FullName}");
        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktopLifetime:
                {
                    StartupTrace.Write("App.OnFrameworkInitializationCompleted desktop lifetime; resolving MainWindowViewModel");
                    var mainWindowViewModel = _services.GetRequiredService<MainWindowViewModel>();
                    StartupTrace.Write("MainWindowViewModel resolved");
                    var notificationManagerProvider = _services.GetRequiredService<INotificationManagerProvider>();
                    var messageForUserTools = _services.GetRequiredService<IMessageForUserTools>();
                    var aboutViewModel = _services.GetRequiredService<AboutViewModel>();
                    StartupTrace.Write("MainWindow dependencies resolved; constructing MainWindow");

                    var mainWindow = new MainWindow(notificationManagerProvider, messageForUserTools, aboutViewModel)
                    {
                        DataContext = mainWindowViewModel
                    };
                    StartupTrace.Write("MainWindow constructed");

                    if (Debugger.IsAttached || !_generalApplicationData.Config.UseSplashScreen)
                    {
                        StartupTrace.Write("showing MainWindow directly");
                        mainWindow.Show();
                        mainWindow.Focus();
                        desktopLifetime.MainWindow = mainWindow;
                        StartupTrace.Write("MainWindow.Show completed and MainWindow assigned");
                    }
                    else
                    {
                        StartupTrace.Write("showing SplashWindow before MainWindow");
                        var simpleLogger = _services.GetRequiredService<JustyBase.PluginCommon.Contracts.ISimpleLogger>();

                        // Splash first; show MainWindow only after it finishes.
                        desktopLifetime.MainWindow = new SplashWindow(() =>
                        {
                            StartupTrace.Write("Splash callback start: assigning MainWindow");
                            desktopLifetime.MainWindow = mainWindow;
                            StartupTrace.Write("Splash callback: MainWindow assigned; calling Show");
                            mainWindow.Show();
                            StartupTrace.Write("Splash callback: Show completed; calling Activate");
                            mainWindow.Activate();
                            StartupTrace.Write("Splash callback: Activate completed; calling Focus");
                            mainWindow.Focus();
                            StartupTrace.Write("Splash callback completed");
                        }, simpleLogger);
                        StartupTrace.Write("SplashWindow constructed and assigned");
                    }
                    break;
                }
        }

        base.OnFrameworkInitializationCompleted();
        StartupTrace.Write("App.OnFrameworkInitializationCompleted completed");
    }
}
