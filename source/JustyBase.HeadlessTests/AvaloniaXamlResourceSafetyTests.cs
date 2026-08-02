using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using JustyBase.Common;
using JustyBase.Common.Contracts;
using JustyBase.Services;
using JustyBase.Themes;
using JustyBase.Views.Documents;
using Moq;

namespace JustyBase.HeadlessTests;

/// <summary>
/// Regression coverage for Avalonia resource/setter crashes
/// (control-as-Setter-value, Color-as-Brush, missing font-size fallbacks).
/// </summary>
public sealed class AvaloniaXamlResourceSafetyTests : HeadlessSessionTestBase
{
    [Fact]
    public Task ApplyFontSizes_WritesDoubleResources() => RunOnUi(() =>
    {
        EnsureThemeManager(themeNum: 0, controlFont: 12.5, completionFont: 14.0);
        FluentThemeManager.ApplyFontSizes();

        Assert.IsType<double>(Application.Current!.Resources["ControlContentThemeFontSize"]);
        Assert.IsType<double>(Application.Current.Resources["CompletitionFontSize"]);
        Assert.Equal(12.5, (double)Application.Current.Resources["ControlContentThemeFontSize"]!);
        Assert.Equal(14.0, (double)Application.Current.Resources["CompletitionFontSize"]!);
    });

    [Fact]
    public Task ThemeAccentResources_AreBrushes_NotColors() => RunOnUi(() =>
    {
        var app = Application.Current!;
        app.Styles.Insert(0, new Avalonia.Themes.Fluent.FluentTheme());
        app.RequestedThemeVariant = ThemeVariant.Light;

        // Seed Fluent color keys that Dock accent brushes are derived from.
        app.Resources["SystemAccentColorLight1"] = Colors.DodgerBlue;
        app.Resources["SystemAccentColorLight2"] = Colors.CornflowerBlue;
        app.Resources["SystemAccentColorLight3"] = Colors.LightSkyBlue;

        EnsureThemeManager(themeNum: 0);
        var manager = FluentThemeManager.StaticFluentThemeManager;
        manager.Initialize(app);

        AssertBrush(app.Resources["DockApplicationAccentBrushLow"]);
        AssertBrush(app.Resources["DockApplicationAccentBrushMed"]);
        AssertBrush(app.Resources["DockApplicationAccentBrushHigh"]);
        AssertBrush(app.Resources["DockApplicationAccentBrushIndicator"]);
    });

    [Fact]
    public Task ToBrush_WrapsColorAsSolidColorBrush() => RunOnUi(() =>
    {
        var brush = FluentThemeManager.ToBrush(Colors.Orange);
        Assert.IsType<SolidColorBrush>(brush);
        Assert.Equal(Colors.Orange, ((SolidColorBrush)brush!).Color);

        var existing = new SolidColorBrush(Colors.Red);
        Assert.Same(existing, FluentThemeManager.ToBrush(existing));
        Assert.Null(FluentThemeManager.ToBrush("not-a-brush"));
    });

    [Fact]
    public Task DocumentTabContextMenuResource_LoadsWithoutException() => RunOnUi(() =>
    {
        var include = new ResourceInclude(new Uri("avares://JustyBase/Themes/SqlDocumentTabContextMenu.axaml"))
        {
            Source = new Uri("avares://JustyBase/Themes/SqlDocumentTabContextMenu.axaml")
        };

        var dictionary = include.Loaded;
        Assert.NotNull(dictionary);
        Assert.True(dictionary!.TryGetResource("DocumentTabStripItemContextMenu", ThemeVariant.Default, out var menu));
        Assert.IsType<ContextMenu>(menu);
    });

    [Fact]
    public Task SettingsView_CanBeCreatedAndShown() => RunOnUi(() =>
    {
        Application.Current!.Resources["SystemAccentColorBrush"] =
            new SolidColorBrush(Colors.DodgerBlue);

        var fonts = Mock.Of<IDocumentFontService>(s =>
            s.GetAvailableFonts() == Array.Empty<FontFamily>());
        var view = new SettingsView(
            Mock.Of<IAvaloniaSpecificHelpers>(),
            fonts)
        {
            Width = 640,
            Height = 480
        };
        var window = new Window
        {
            Width = 700,
            Height = 520,
            Content = view,
            Title = "SettingsView resource safety"
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(window.CaptureRenderedFrame());
        Assert.Same(view, window.Content);
    });

    [Fact]
    public Task CompletionListFontSize_ResolvesAsDouble() => RunOnUi(() =>
    {
        Application.Current!.Resources["CompletitionFontSize"] = 13.0;
        Application.Current.Resources["ControlContentThemeFontSize"] = 12.0;

        var text = new TextBlock
        {
            Text = "SELECT",
            FontSize = (double)Application.Current.FindResource("CompletitionFontSize")!
        };

        Assert.Equal(13.0, text.FontSize);
        Assert.IsType<double>(Application.Current.FindResource("ControlContentThemeFontSize"));
    });

    private static void EnsureThemeManager(int themeNum, double controlFont = 12, double completionFont = 13)
    {
        var appData = new Mock<IGeneralApplicationData>();
        appData.SetupGet(a => a.Config).Returns(new AppOptions
        {
            ThemeNum = themeNum,
            ControlContentThemeFontSize = controlFont,
            CompletitionFontSize = completionFont
        });
        _ = new FluentThemeManager(appData.Object);
    }

    private static void AssertBrush(object? value)
    {
        Assert.NotNull(value);
        Assert.IsAssignableFrom<IBrush>(value);
        Assert.IsNotType<Color>(value);
    }
}
