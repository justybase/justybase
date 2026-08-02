using JustyBase.ViewModels;

namespace JustyBase.Views;

public partial class About : Window
{
    public About(AboutViewModel viewModel)
    {
        InitializeComponent();
        this.KeyDown += About_KeyDown;
        this.DataContext = viewModel;
    }

    private void About_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Escape)
        {
            (sender as Window)?.Close();
        }
        else if (e.Key == Avalonia.Input.Key.Return)
        {
            (sender as Window)?.Close();
        }
    }
}