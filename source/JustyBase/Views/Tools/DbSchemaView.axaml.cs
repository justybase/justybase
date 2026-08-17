using JustyBase.Services;
using JustyBase.Services.Database;
using JustyBase.ViewModels.Tools;

namespace JustyBase.Views.Tools;

public partial class DbSchemaView : UserControl
{
    private readonly IAvaloniaSpecificHelpers _avaloniaSpecificHelpers;
    private readonly AddNewConnectionViewModel _addNewConnectionViewModel;

    public DbSchemaView(IAvaloniaSpecificHelpers avaloniaSpecificHelpers, AddNewConnectionViewModel addNewConnectionViewModel)
    {
        _avaloniaSpecificHelpers = avaloniaSpecificHelpers;
        _addNewConnectionViewModel = addNewConnectionViewModel;
        InitializeComponent();
        Initialized += DbSchemaView_Initialized;
        btAddNewConnection.Click += BtAddNewConnection_Click;
        btConnectionsSettings.Click += BtConnectionsSettings_Click;
        cmSchema.Opening += SchemaContextMenu_ContextMenuOpening;
    }

    private async void BtConnectionsSettings_Click(object? sender, RoutedEventArgs e)
    {
        var vmX = _addNewConnectionViewModel;
        vmX.ShowExistings = true;

        var wn = new Window()
        {
            Content = new AddNewConnectionView()
            {
                DataContext = vmX
            },
            Width = 900,
            MinWidth = 680,
            MinHeight = 560,
            SizeToContent = SizeToContent.Height,
            Title = "Connections",
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowActivated = true,
            ShowInTaskbar = true,
            UseLayoutRounding = true,
            CornerRadius = new Avalonia.CornerRadius(5),
            ExtendClientAreaToDecorationsHint = false,
            WindowDecorations = WindowDecorations.Full,
            CanResize = true
        };
        vmX.CloseWindowAction = () => wn.Close();
        wn.KeyDown += Wn_KeyDown;

        await wn.ShowDialog(_avaloniaSpecificHelpers.GetMainWindow());
    }

    public async Task AddNewConnectionWindow()
    {
        var vmX = _addNewConnectionViewModel;
        vmX.ShowExistings = false;

        var wn = new Window()
        {
            Content = new AddNewConnectionView() { DataContext = vmX },
            Width = 900,
            MinWidth = 680,
            MinHeight = 560,
            SizeToContent = SizeToContent.Height,
            Title = "Add connection",
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowActivated = true,
            ShowInTaskbar = true,
            UseLayoutRounding = true,
            CornerRadius = new Avalonia.CornerRadius(5),
            ExtendClientAreaToDecorationsHint = false,
            WindowDecorations = WindowDecorations.Full,
            CanResize = true
        };
        vmX.CloseWindowAction = () => wn.Close();
        wn.KeyDown += Wn_KeyDown;

        await wn.ShowDialog(_avaloniaSpecificHelpers.GetMainWindow());
    }
    private async void BtAddNewConnection_Click(object? sender, RoutedEventArgs e)
    {
        await AddNewConnectionWindow();
    }

    private static void Wn_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key is Key.Escape)
        {
            (sender as Window)?.Close();
        }
    }

    private DbSchemaViewModel ViewModel => this.DataContext as DbSchemaViewModel;
    private void DbSchemaView_Initialized(object? sender, System.EventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        ViewModel.FocusAndBringSelectionIntoView = () =>
        {
            dbSchemaTreeGrid.Focus();

            // Prefer the grid's current SelectedItem (may be HierarchicalNode after binding sync).
            var selected = dbSchemaTreeGrid.SelectedItem;
            if (selected is not null)
            {
                dbSchemaTreeGrid.ScrollIntoView(selected, null);
                return;
            }

            if (ViewModel.SelectedSchemaItem is not null)
            {
                dbSchemaTreeGrid.ScrollIntoView(ViewModel.SelectedSchemaItem, null);
            }
        };
        dbSchemaTreeGrid.DoubleTapped += DbSchemaView_DoubleTapped;
    }

    private void DbSchemaView_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (dbSchemaTreeGrid.SelectedItem is null)
        {
            return;
        }
        
        // ProDataGrid returns HierarchicalNode, extract the actual item
        var selectedItem = dbSchemaTreeGrid.SelectedItem;
        if (selectedItem is Avalonia.Controls.DataGridHierarchical.HierarchicalNode node && node.Item is IDatabaseSchemaItem schemaModel)
        {
            IDatabaseSchemaItem.InsertDoubleClicked(schemaModel);
        }
        else if (selectedItem is IDatabaseSchemaItem schemaModel2)
        {
            IDatabaseSchemaItem.InsertDoubleClicked(schemaModel2);
        }
    }
    
    private void DbSchemaTreeGrid_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        // Convert HierarchicalNode to DbSchemaModel for the ViewModel
        if (dbSchemaTreeGrid.SelectedItem is Avalonia.Controls.DataGridHierarchical.HierarchicalNode node)
        {
            if (this.ViewModel is not null && node.Item is JustyBase.Models.Tools.DbSchemaModel model)
            {
                this.ViewModel.SelectedSchemaItem = model;
            }
        }
        else if (dbSchemaTreeGrid.SelectedItem is JustyBase.Models.Tools.DbSchemaModel model)
        {
            if (this.ViewModel is not null)
            {
                this.ViewModel.SelectedSchemaItem = model;
            }
        }
    }
    
    private void SchemaContextMenu_ContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        this.ViewModel.PrepareContextMenu(null);
    }
}
