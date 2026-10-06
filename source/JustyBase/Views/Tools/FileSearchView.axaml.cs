using System.ComponentModel;
using JustyBase.ViewModels.Tools;

namespace JustyBase.Views.Tools;

public partial class FileSearchView : UserControl
{
    public FileSearchView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private FileSearchViewModel? _viewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as FileSearchViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Ctrl+Shift+F (or any caller) can request keyboard focus in the search box.
        if (string.Equals(e.PropertyName, nameof(FileSearchViewModel.FocusSearchRequest), StringComparison.Ordinal))
        {
            PatternBox.Focus();
            PatternBox.SelectAll();
        }
    }

    private FileSearchViewModel? ViewModel => DataContext as FileSearchViewModel;

    private void OpenHit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ContentSearchHitRow row)
        {
            ViewModel?.OpenHit(row);
        }
    }

    private async void ReplaceHit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ContentSearchHitRow row && ViewModel is { } vm)
        {
            await vm.ReplaceAsync(hit: row);
        }
    }

    private async void ReplaceFile_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ContentSearchFileGroup group && ViewModel is { } vm)
        {
            await vm.ReplaceAsync(group: group);
        }
    }

    private async void ReplaceAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            await vm.ReplaceAsync();
        }
    }
}
