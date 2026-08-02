using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

namespace JustyBase.HeadlessTests;

internal sealed class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://LiveMarkdown.Avalonia/Styles.axaml"))
        {
            Source = new Uri("avares://LiveMarkdown.Avalonia/Styles.axaml")
        });
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://LiveMarkdown.Avalonia/Defaults.axaml"))
        {
            Source = new Uri("avares://LiveMarkdown.Avalonia/Defaults.axaml")
        });
    }
}

internal static class HeadlessAppSetup
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            });
}
