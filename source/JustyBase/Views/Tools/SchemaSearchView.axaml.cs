using JustyBase.Common.Models;
using JustyBase.ViewModels.Tools;

namespace JustyBase.Views.Tools;

public partial class SchemaSearchView : UserControl, ISchemaSearchViewBridge
{
    private SchemaSearchViewModel? _boundViewModel;

    public SchemaSearchView()
    {
        InitializeComponent();
        SchemaSearchDataGrid.LoadingRowGroup += Dg_LoadingRowGroup;
        SchemaSearchDataGrid.SelectionChanged += Dg_SelectionChanged;
        SchemaSearchDataGrid.DoubleTapped += Dg_DoubleTapped;
        this.Initialized += SchemaSearchView_Initialized;
        DataContextChanged += SchemaSearchView_DataContextChanged;
        DetachedFromVisualTree += SchemaSearchView_DetachedFromVisualTree;
    }

    private SchemaSearchViewModel? ViewModel => this.DataContext as SchemaSearchViewModel;
    private async void Dg_DoubleTapped(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SchemaSearchDataGrid.SelectedItem is SchemaSearchItem searchItem)
        {
            await this.ViewModel?.DoubleTappedAction(searchItem);
        }
    }

    private void Dg_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SchemaSearchDataGrid.SelectedItem is not SchemaSearchItem selectedItem)
        {
            return;
        }

        // The details row becomes visible as a result of the selection binding. Defer
        // the scroll until that row has been laid out, otherwise the last clicked row
        // can end up underneath the details panel on the first click.
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(SchemaSearchDataGrid.SelectedItem, selectedItem))
            {
                SchemaSearchDataGrid.ScrollIntoView(selectedItem, null);
            }
        }, DispatcherPriority.Loaded);
    }

    private async void SchemaSearchView_Initialized(object? sender, System.EventArgs e)
    {
        if (ViewModel is not null)
        {
            bool firstTime = false;
            if (ViewModel.ViewBridge is null)
            {
                firstTime = true;
            }
            BindViewBridge(ViewModel);
            if (firstTime && ViewModel.RefreshStartup)
            {
                await ViewModel.RefreshDbCmd.ExecuteAsync(null);
            }
            else
            {
                ViewModel.TryGoupResults(SchemaSearchViewModel.GIANT_GROUP_LIMIT);
            }
        }
    }

    private void SchemaSearchView_DataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is SchemaSearchViewModel vm)
        {
            BindViewBridge(vm);
        }
        else
        {
            UnbindViewBridge();
        }
    }

    private void SchemaSearchView_DetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        UnbindViewBridge();
    }

    private void BindViewBridge(SchemaSearchViewModel vm)
    {
        if (!ReferenceEquals(_boundViewModel, vm))
        {
            if (_boundViewModel is not null && ReferenceEquals(_boundViewModel.ViewBridge, this))
            {
                _boundViewModel.ViewBridge = null;
            }

            _boundViewModel = vm;
        }

        vm.ViewBridge = this;
    }

    private void UnbindViewBridge()
    {
        if (_boundViewModel is not null && ReferenceEquals(_boundViewModel.ViewBridge, this))
        {
            _boundViewModel.ViewBridge = null;
        }

        _boundViewModel = null;
    }

    private void Dg_LoadingRowGroup(object? sender, DataGridRowGroupHeaderEventArgs e)
    {
        if (e.RowGroupHeader is DataGridRowGroupHeader groupHeader)
        {
            groupHeader.IsItemCountVisible = true;
            groupHeader.ItemCountFormat = "({0:N0} Items)";
        }
    }

    public void CollapseAllGroups()
    {
        SchemaSearchDataGrid.CollapseAllGroups();
    }
    
    public void ExpandAllGroups()
    {
        SchemaSearchDataGrid.ExpandAllGroups();
    }
}
