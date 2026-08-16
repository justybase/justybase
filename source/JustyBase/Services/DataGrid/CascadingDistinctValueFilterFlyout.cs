using System.Collections;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using DataGridControl = Avalonia.Controls.DataGrid;

namespace JustyBase.Services.DataGrid;

/// <summary>
/// Experimental distinct-value flyout that reads options from the current
/// filtered view instead of the unfiltered source collection.
/// </summary>
public sealed class CascadingDistinctValueFilterFlyout : Flyout
{
    private CascadingDistinctValueFilterContext? _context;
    private IFilteringModel? _contextModel;
    private object? _contextColumnId;
    private IDataGridColumnValueAccessor? _contextAccessor;
    private IEqualityComparer? _contextComparer;
    private Func<object?, string>? _contextFormatter;

    public DataGridColumn? Column { get; set; }

    public IDataGridColumnValueAccessor? ValueAccessor { get; set; }

    public IEqualityComparer? ValueComparer { get; set; }

    public Func<object?, string>? DisplayFormatter { get; set; }

    public string? LastError { get; private set; }

    public CascadingDistinctValueFilterContext? Context => _context;

    protected override void OnOpening(CancelEventArgs args)
    {
        DataGridControl? grid = FindGrid(Target);
        if (Column is null || grid is null || ValueAccessor is null)
        {
            LastError = "The cascading distinct-value filter must have a DataGrid column and value accessor.";
            args.Cancel = true;
            base.OnOpening(args);
            return;
        }

        object columnId = Column.ColumnKey ?? Convert.ToString(Column.Header, CultureInfo.InvariantCulture) ?? "column";
        IFilteringModel filteringModel = grid.FilteringModel;
        string label = Convert.ToString(Column.Header, CultureInfo.InvariantCulture) ?? "Values";
        string? propertyPath = Column.SortMemberPath;

        if (_context is null ||
            !ReferenceEquals(_contextModel, filteringModel) ||
            !Equals(_contextColumnId, columnId) ||
            !ReferenceEquals(_contextAccessor, ValueAccessor) ||
            !ReferenceEquals(_contextComparer, ValueComparer) ||
            !ReferenceEquals(_contextFormatter, DisplayFormatter))
        {
            _context = new CascadingDistinctValueFilterContext(
                filteringModel,
                columnId,
                ValueAccessor,
                label,
                propertyPath,
                ValueComparer,
                DisplayFormatter);
            _contextModel = filteringModel;
            _contextColumnId = columnId;
            _contextAccessor = ValueAccessor;
            _contextComparer = ValueComparer;
            _contextFormatter = DisplayFormatter;
        }

        // The current collection view enumerates rows after its active filter.
        // Passing it directly makes distinct options dependent on other columns.
        _context.Refresh(grid.ItemsSource as IEnumerable);
        Content = _context;
        ResolveResources(grid);
        LastError = null;
        base.OnOpening(args);
    }

    private void ResolveResources(DataGridControl grid)
    {
        if (ContentTemplate is null &&
            grid.TryFindResource("JustyCascadingFilterTemplate", out object? templateResource) &&
            templateResource is IDataTemplate template)
        {
            ContentTemplate = template;
        }

        if (FlyoutPresenterTheme is null &&
            grid.TryFindResource("DataGridFilterFlyoutPresenterTheme", out object? themeResource) &&
            themeResource is ControlTheme theme)
        {
            FlyoutPresenterTheme = theme;
        }
    }

    private static DataGridControl? FindGrid(Control? target)
    {
        Visual? current = target;
        while (current is not null)
        {
            if (current is DataGridControl grid)
            {
                return grid;
            }

            current = current.GetVisualParent();
        }

        return null;
    }
}
