using JustyBase.ViewModels.Tools;

namespace JustyBase.Views.Tools;

public partial class VariablesView : UserControl
{
    public VariablesView()
    {
        InitializeComponent();
    }

    private VariablesViewModel? ViewModel => DataContext as VariablesViewModel;

    //referenced in xaml
    private void VariablesDataGrid_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Delete:
                ViewModel?.RemoveSelectedVariable();
                break;
            case Key.OemPlus or Key.Add:
                ViewModel?.AddNewVariable();
                break;
            case Key.F5:
                ViewModel?.RefreshVariables();
                break;
        }
    }

    //referenced in xaml
    private void VariablesDataGrid_DoubleTapped(object sender, RoutedEventArgs e)
    {
        ViewModel?.DataGridDoubleClicked();
    }

    private void Add_Click(object? sender, RoutedEventArgs e) => ViewModel?.AddNewVariable();
    private void Remove_Click(object? sender, RoutedEventArgs e) => ViewModel?.RemoveSelectedVariable();
    private void Refresh_Click(object? sender, RoutedEventArgs e) => ViewModel?.RefreshVariables();
}
