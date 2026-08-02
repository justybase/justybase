using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using JustyBase.Common.Contracts;
using JustyBase.Editor;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.Helpers.Interactions;
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
        var collection = new ServiceCollection();
        collection.AddCommonServices();
        _services = collection.BuildServiceProvider();
        Program.SetServiceProvider(_services);
        // The SQL template is deliberately non-recycling. Dock's document-content cache keeps
        // one editor view per open SQL tab, preserving its visual state across tab switches.
        DataTemplates.Add(new SqlDocumentDataTemplate(_services));
        DataTemplates.Add(new ViewLocator(_services));
        _generalApplicationData = _services.GetRequiredService<IGeneralApplicationData>();

        _themeManager = _services.GetRequiredService<IThemeManager>();
        _themeManager.Initialize(this);
        AvaloniaXamlLoader.Load(this);

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

        SemanticLineColorizer.Configure(_services.GetRequiredService<NzSemanticTokenClassifier>());
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
        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktopLifetime:
                {
                    var mainWindowViewModel = _services.GetRequiredService<MainWindowViewModel>();
                    var notificationManagerProvider = _services.GetRequiredService<INotificationManagerProvider>();
                    var messageForUserTools = _services.GetRequiredService<IMessageForUserTools>();
                    var aboutViewModel = _services.GetRequiredService<AboutViewModel>();

                    var mainWindow = new MainWindow(notificationManagerProvider, messageForUserTools, aboutViewModel)
                    {
                        DataContext = mainWindowViewModel
                    };

                    if (Debugger.IsAttached || !_generalApplicationData.Config.UseSplashScreen)
                    {
                        mainWindow.Show();
                        mainWindow.Focus();
                        desktopLifetime.MainWindow = mainWindow;
                    }
                    else
                    {
                        var simpleLogger = _services.GetRequiredService<JustyBase.PluginCommon.Contracts.ISimpleLogger>();

                        // Splash first; show MainWindow only after it finishes.
                        desktopLifetime.MainWindow = new SplashWindow(() =>
                        {
                            desktopLifetime.MainWindow = mainWindow;
                            mainWindow.Show();
                            mainWindow.Activate();
                            mainWindow.Focus();
                        }, simpleLogger);
                    }
                    break;
                }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
