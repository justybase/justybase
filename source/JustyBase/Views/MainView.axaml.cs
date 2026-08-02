using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using JustyBase.Common.Contracts;
using JustyBase.Editor;
using JustyBase.Themes;
using JustyBase.ViewModels;
using System.Runtime.InteropServices;

namespace JustyBase.Views;

public partial class MainView : UserControl
{
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IThemeManager _themeManager;

    // XAML-instantiated from MainWindow.axaml; DI ctor injection not available without changing view creation.
    public MainView() : this(App.GetRequiredService<IGeneralApplicationData>(), App.GetRequiredService<IThemeManager>())
    {
    }

    public MainView(IGeneralApplicationData generalApplicationData, IThemeManager themeManager)
    {
        _generalApplicationData = generalApplicationData;
        _themeManager = themeManager;
        InitializeComponent();
        InitializeThemes();
        ApplyMacOsOffsets();
    }

    private void ApplyMacOsOffsets()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            TopNavBar.Margin = new Thickness(80, 0, 0, 0);
        }
    }
    private void InitializeThemes()
    {
        ThemeButton.Click += (_, _) => ChangeTheme();
    }

    private void ViewMenu_SubmenuOpened(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.RefreshViewPanelStates();
        }
    }

    public void ChangeTheme()
    {
        _generalApplicationData.Config.ThemeNum = 1 - _generalApplicationData.Config.ThemeNum;
        var isDark = FluentThemeManager.IsDark;
        _themeManager?.Switch(isDark ? 1 : 0);
        SqlCodeEditorHelpers.ResetStyle(isDark);
        if (isDark)
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
}
