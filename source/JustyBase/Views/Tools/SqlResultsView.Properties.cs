using System.Collections;
using Avalonia.Data;
using JustyBase.Models;

namespace JustyBase.Views.Tools;

/// <summary>
/// StyledProperty and DirectProperty declarations for SqlResultsView.
/// Separated into a partial class for organization.
/// </summary>
public sealed partial class SqlResultsView
{
    static SqlResultsView()
    {
        CurrentResultsTableProperty.Changed.AddClassHandler<SqlResultsView>(OnCurrentResultsTableChanged);
    }

    private static void OnCurrentResultsTableChanged(SqlResultsView view, AvaloniaPropertyChangedEventArgs args)
    {
        // Refresh columns when CurrentResultsTable changes (e.g., when data is loaded)
        if (args.NewValue is TableOfSqlResults newTable && newTable.Headers.Count > 0)
        {
            view.RefreshDataGridColumns();
        }
    }
    public static readonly StyledProperty<ICommand> ShowFlyoutCommandProperty =
        AvaloniaProperty.Register<SqlResultsView, ICommand>(nameof(ShowFlyoutCommand));

    public ICommand ShowFlyoutCommand
    {
        get => GetValue(ShowFlyoutCommandProperty);
        set => SetValue(ShowFlyoutCommandProperty, value);
    }

    public static readonly StyledProperty<ICommand> Copy1CommandProperty =
        AvaloniaProperty.Register<SqlResultsView, ICommand>(nameof(Copy1Command));

    public ICommand Copy1Command
    {
        get => GetValue(Copy1CommandProperty);
        set => SetValue(Copy1CommandProperty, value);
    }

    public static readonly StyledProperty<TableOfSqlResults> CurrentResultsTableProperty =
        AvaloniaProperty.Register<SqlResultsView, TableOfSqlResults>(nameof(CurrentResultsTable));

    public TableOfSqlResults CurrentResultsTable
    {
        get => GetValue(CurrentResultsTableProperty);
        set => SetValue(CurrentResultsTableProperty, value);
    }

    public static readonly StyledProperty<ICommand> ChangeColumVisiblityCommandProperty =
        AvaloniaProperty.Register<SqlResultsView, ICommand>(nameof(ChangeColumVisiblityCommand));

    public ICommand ChangeColumVisiblityCommand
    {
        get => GetValue(ChangeColumVisiblityCommandProperty);
        set => SetValue(ChangeColumVisiblityCommandProperty, value);
    }

    public static readonly StyledProperty<ICommand> TriggerSearchTimerCommandProperty =
        AvaloniaProperty.Register<SqlResultsView, ICommand>(nameof(TriggerSearchTimerCommand));

    public ICommand TriggerSearchTimerCommand
    {
        get => GetValue(TriggerSearchTimerCommandProperty);
        set => SetValue(TriggerSearchTimerCommandProperty, value);
    }

    public static readonly StyledProperty<ICommand> GridDoubleClickCommandProperty =
        AvaloniaProperty.Register<SqlResultsView, ICommand>(nameof(GridDoubleClickCommand));

    public ICommand GridDoubleClickCommand
    {
        get => GetValue(GridDoubleClickCommandProperty);
        set => SetValue(GridDoubleClickCommandProperty, value);
    }

    public static readonly StyledProperty<ICommand> GridSelectionChangedCommandProperty =
        AvaloniaProperty.Register<SqlResultsView, ICommand>(nameof(GridSelectionChangedCommand));

    public ICommand GridSelectionChangedCommand
    {
        get => GetValue(GridSelectionChangedCommandProperty);
        set => SetValue(GridSelectionChangedCommandProperty, value);
    }

    public static readonly StyledProperty<IEnumerable<object>> SelectedColumnCellsProperty =
        AvaloniaProperty.Register<SqlResultsView, IEnumerable<object>>(nameof(SelectedColumnCells));

    public IEnumerable<object> SelectedColumnCells
    {
        get => GetValue(SelectedColumnCellsProperty);
        set => SetValue(SelectedColumnCellsProperty, value);
    }

    public static readonly DirectProperty<SqlResultsView, IList> SelectedItemsProperty =
        AvaloniaProperty.RegisterDirect<SqlResultsView, IList>(nameof(SelectedItems), x => x.SelectedItems, defaultBindingMode: BindingMode.OneWayToSource);

    public IList SelectedItems
    {
        get => ResultDataGrid.SelectedItems;
    }

    public static readonly DirectProperty<SqlResultsView, Dictionary<int, AditionalOneFilter>> AdditionalValuesProperty =
        AvaloniaProperty.RegisterDirect<SqlResultsView, Dictionary<int, AditionalOneFilter>>(nameof(AdditionalValues),
            x => x.AdditionalValues,
            (o, v) => o.AdditionalValues = v, defaultBindingMode: BindingMode.OneTime);

    private Dictionary<int, AditionalOneFilter> _additionalValues = [];
    public Dictionary<int, AditionalOneFilter> AdditionalValues
    {
        get => _additionalValues;
        set => SetAndRaise(AdditionalValuesProperty, ref _additionalValues, value);
    }

    public static readonly StyledProperty<string> StatsTextProperty =
        AvaloniaProperty.Register<SqlResultsView, string>(nameof(StatsText));

    public string StatsText
    {
        get => GetValue(StatsTextProperty);
        set => SetValue(StatsTextProperty, value);
    }
}
