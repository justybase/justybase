using JustyBase.ViewModels;

namespace JustyBase;

public partial class SnippetControl : UserControl
{
    // XAML-instantiated from SnippetWindow.axaml; DI ctor injection not available without changing view creation.
    public SnippetControl() : this(App.GetRequiredService<SnippetControlViewModel>())
    {
    }

    public SnippetControl(SnippetControlViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}