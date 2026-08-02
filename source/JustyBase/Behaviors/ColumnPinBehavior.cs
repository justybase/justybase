using Avalonia.Xaml.Interactivity;

namespace JustyBase.Behaviors;

public sealed class ColumnPinBehavior : Behavior<Button>
{
    public static readonly StyledProperty<string> ColumnNameProperty =
        AvaloniaProperty.Register<ColumnPinBehavior, string>(nameof(ColumnName));

    public static readonly StyledProperty<int> ColumnIndexProperty =
        AvaloniaProperty.Register<ColumnPinBehavior, int>(nameof(ColumnIndex));

    public static readonly StyledProperty<DataGrid> DataGridProperty =
        AvaloniaProperty.Register<ColumnPinBehavior, DataGrid>(nameof(DataGrid));

    public static readonly StyledProperty<bool> IsPinnedProperty =
        AvaloniaProperty.Register<ColumnPinBehavior, bool>(nameof(IsPinned));

    public string ColumnName
    {
        get => GetValue(ColumnNameProperty);
        set => SetValue(ColumnNameProperty, value);
    }

    public int ColumnIndex
    {
        get => GetValue(ColumnIndexProperty);
        set => SetValue(ColumnIndexProperty, value);
    }

    public DataGrid DataGrid
    {
        get => GetValue(DataGridProperty);
        set => SetValue(DataGridProperty, value);
    }

    public bool IsPinned
    {
        get => GetValue(IsPinnedProperty);
        set => SetValue(IsPinnedProperty, value);
    }

    private readonly Dictionary<string, int> _pinnedColumns = [];

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject != null)
        {
            AssociatedObject.Click += OnPinClick;
            UpdateButtonAppearance();
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            AssociatedObject.Click -= OnPinClick;
        }
        base.OnDetaching();
    }

    private void OnPinClick(object? sender, RoutedEventArgs e)
    {
        if (DataGrid == null || string.IsNullOrEmpty(ColumnName))
            return;

        if (IsPinned)
        {
            UnpinColumn();
        }
        else
        {
            PinColumn();
        }

        UpdateButtonAppearance();
    }

    private void PinColumn()
    {
        _pinnedColumns[ColumnName] = DataGrid.FrozenColumnCount;
        var column = DataGrid.Columns[ColumnIndex];
        column.DisplayIndex = _pinnedColumns.Count - 1;

        if (column is DataGridTextColumn textColumn)
        {
            textColumn.FontWeight = FontWeight.UltraBold;
        }

        DataGrid.FrozenColumnCount = _pinnedColumns.Count;
        IsPinned = true;
        RefreshColumnVisibility();
    }

    private void UnpinColumn()
    {
        _pinnedColumns.Remove(ColumnName);
        var column = DataGrid.Columns[ColumnIndex];
        column.DisplayIndex = _pinnedColumns.Count;

        if (column is DataGridTextColumn textColumn)
        {
            textColumn.FontWeight = FontWeight.Normal;
        }

        DataGrid.FrozenColumnCount = _pinnedColumns.Count;
        IsPinned = false;
        RefreshColumnVisibility();
    }

    private void RefreshColumnVisibility()
    {
        DataGrid.Columns[ColumnIndex].IsVisible = false;
        DataGrid.Columns[ColumnIndex].IsVisible = true;
    }

    private void UpdateButtonAppearance()
    {
        if (AssociatedObject == null)
            return;

        var pinnedIcon = AssociatedObject.FindResource("btPinData") as StreamGeometry;
        var unpinnedIcon = AssociatedObject.FindResource("btPinData2") as StreamGeometry;

        AssociatedObject.Content = new PathIcon
        {
            Data = IsPinned ? pinnedIcon : unpinnedIcon
        };
    }
}
