using JustyBase.ViewModels;

namespace JustyBase.Views.OtherDialogs;

public partial class FileDiffWindow : Window
{
    private bool _isSyncingScroll = false;

    public FileDiffWindow()
    {
        InitializeComponent();
        
        if (OldScrollViewer != null)
        {
            OldScrollViewer.AddHandler(ScrollViewer.ScrollChangedEvent, OnOldScrollChanged);
        }
        if (NewScrollViewer != null)
        {
            NewScrollViewer.AddHandler(ScrollViewer.ScrollChangedEvent, OnNewScrollChanged);
        }
        
        this.DataContextChanged += FileDiffWindow_DataContextChanged;
    }

    private void FileDiffWindow_DataContextChanged(object? sender, System.EventArgs e)
    {
        if (this.DataContext is FileDiffViewModel vm)
        {
            vm.CloseAction = () => this.Close();
        }
    }

    private void OnOldScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isSyncingScroll || NewScrollViewer == null) return;
        _isSyncingScroll = true;
        NewScrollViewer.Offset = OldScrollViewer.Offset;
        _isSyncingScroll = false;
    }

    private void OnNewScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isSyncingScroll || OldScrollViewer == null) return;
        _isSyncingScroll = true;
        OldScrollViewer.Offset = NewScrollViewer.Offset;
        _isSyncingScroll = false;
    }
}
