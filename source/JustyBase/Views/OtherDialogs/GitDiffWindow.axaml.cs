using JustyBase.ViewModels;

namespace JustyBase.Views.OtherDialogs;

public partial class GitDiffWindow : Window
{
    private bool _isSyncingScroll;

    public GitDiffWindow()
    {
        InitializeComponent();

        if (OldScrollViewer != null)
            OldScrollViewer.AddHandler(ScrollViewer.ScrollChangedEvent, OnOldScrollChanged);
        if (NewScrollViewer != null)
            NewScrollViewer.AddHandler(ScrollViewer.ScrollChangedEvent, OnNewScrollChanged);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is GitDiffViewModel vm)
                vm.CloseAction = Close;
        };
    }

    private void OnOldScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isSyncingScroll || NewScrollViewer == null)
            return;
        _isSyncingScroll = true;
        NewScrollViewer.Offset = OldScrollViewer.Offset;
        _isSyncingScroll = false;
    }

    private void OnNewScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isSyncingScroll || OldScrollViewer == null)
            return;
        _isSyncingScroll = true;
        OldScrollViewer.Offset = NewScrollViewer.Offset;
        _isSyncingScroll = false;
    }
}
