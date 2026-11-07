#if AI
using Avalonia;
using Avalonia.Markup.Xaml.Styling;

namespace JustyBase;

// LiveMarkdown resources are loaded at runtime (URI only, no compile-time
// dependency on the package) so No-AI/Slim builds compile without it.
// Previously these lived inline in App.axaml.
public partial class App
{
    private void LoadLiveMarkdownResources()
    {
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://JustyBase/App.axaml"))
        {
            Source = new Uri("avares://LiveMarkdown.Avalonia/Defaults.axaml")
        });
        Styles.Add(new StyleInclude(new Uri("avares://JustyBase/App.axaml"))
        {
            Source = new Uri("avares://JustyBase/Themes/LiveMarkdownStyles.axaml")
        });
    }
}
#endif
