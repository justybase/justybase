using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Controls.DataGridFiltering;

namespace JustyBase.Services.DataGrid;

/// <summary>
/// Distinct-value filter context used by the experimental results-grid filter.
/// Values are refreshed from the current view, so filters in other columns narrow
/// the options shown by this filter.
/// </summary>
public sealed class CascadingDistinctValueFilterContext : IFilterDistinctValuesContext, INotifyPropertyChanged
{
    private static readonly object NullKey = new();

    private readonly IFilteringModel _filteringModel;
    private readonly object _columnId;
    private readonly string? _propertyPath;
    private readonly IDataGridColumnValueAccessor _valueAccessor;
    private readonly IEqualityComparer _valueComparer;
    private readonly bool _usesCustomValueComparer;
    private readonly Func<object?, string> _displayFormatter;
    private readonly List<CascadingDistinctValueFilterOption> _allOptions = [];

    private object _activeDescriptorColumnId;
    private string? _searchText;
    private bool _suppressFilterUpdates;

    public CascadingDistinctValueFilterContext(
        IFilteringModel filteringModel,
        object columnId,
        IDataGridColumnValueAccessor valueAccessor,
        string label,
        string? propertyPath = null,
        IEqualityComparer? valueComparer = null,
        Func<object?, string>? displayFormatter = null)
    {
        _filteringModel = filteringModel ?? throw new ArgumentNullException(nameof(filteringModel));
        _columnId = columnId ?? throw new ArgumentNullException(nameof(columnId));
        _activeDescriptorColumnId = _columnId;
        _valueAccessor = valueAccessor ?? throw new ArgumentNullException(nameof(valueAccessor));
        _usesCustomValueComparer = valueComparer is not null;
        _valueComparer = valueComparer ?? EqualityComparer<object>.Default;
        _displayFormatter = displayFormatter ?? FormatDisplayValue;
        Label = label ?? string.Empty;
        Options = [];
        ClearAllCommand = new ActionCommand(ClearAll);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label { get; }

    public string? SearchText
    {
        get => _searchText;
        set
        {
            if (string.Equals(_searchText, value, StringComparison.Ordinal))
            {
                return;
            }

            _searchText = value;
            OnPropertyChanged();
            ApplySearch();
        }
    }

    public ObservableCollection<IFilterDistinctValueOption> Options { get; }

    public ICommand ClearAllCommand { get; }

    /// <summary>
    /// Rebuilds options from the supplied enumerable. The flyout passes the
    /// current collection view rather than its SourceCollection, which gives
    /// the filter Excel-like dependent option lists.
    /// </summary>
    public void Refresh(IEnumerable? items)
    {
        var counts = new Dictionary<object, ValueCount>(new NullKeyComparer(_valueComparer));
        if (items is not null)
        {
            foreach (object? item in items)
            {
                object? value = _valueAccessor.GetValue(item!);
                object key = value ?? NullKey;
                if (counts.TryGetValue(key, out ValueCount? entry))
                {
                    entry.Count++;
                }
                else
                {
                    counts.Add(key, new ValueCount(value));
                }
            }
        }

        FilteringDescriptor? activeDescriptor = FindActiveDescriptor();
        _activeDescriptorColumnId = activeDescriptor?.ColumnId ?? _columnId;
        IReadOnlyList<object>? selectedValues = activeDescriptor?.Operator == FilteringOperator.In
            ? activeDescriptor.Values
            : null;

        // Keep selected values visible even when the current view no longer has
        // a row for one of them. This preserves checkbox state on reopen.
        if (selectedValues is not null)
        {
            for (int i = 0; i < selectedValues.Count; i++)
            {
                object? selectedValue = selectedValues[i];
                object key = selectedValue ?? NullKey;
                if (!counts.ContainsKey(key))
                {
                    counts.Add(key, new ValueCount(selectedValue, 0));
                }
            }
        }

        var nextOptions = new List<CascadingDistinctValueFilterOption>(counts.Count);
        foreach (ValueCount entry in counts.Values)
        {
            nextOptions.Add(new CascadingDistinctValueFilterOption(
                entry.Value,
                _displayFormatter(entry.Value),
                entry.Count,
                Contains(selectedValues, entry.Value),
                OnOptionSelectionChanged));
        }

        nextOptions.Sort(static (left, right) =>
            StringComparer.CurrentCultureIgnoreCase.Compare(left.Display, right.Display));

        _suppressFilterUpdates = true;
        try
        {
            _allOptions.Clear();
            _allOptions.AddRange(nextOptions);
            ApplySearch();
        }
        finally
        {
            _suppressFilterUpdates = false;
        }
    }

    private void ClearAll()
    {
        _suppressFilterUpdates = true;
        try
        {
            for (int i = 0; i < _allOptions.Count; i++)
            {
                _allOptions[i].IsSelected = false;
            }
        }
        finally
        {
            _suppressFilterUpdates = false;
        }

        _filteringModel.Remove(_activeDescriptorColumnId);
        _activeDescriptorColumnId = _columnId;
    }

    private void OnOptionSelectionChanged()
    {
        if (_suppressFilterUpdates)
        {
            return;
        }

        var selectedValues = new List<object>();
        for (int i = 0; i < _allOptions.Count; i++)
        {
            CascadingDistinctValueFilterOption option = _allOptions[i];
            if (option.IsSelected)
            {
                selectedValues.Add(option.Value!);
            }
        }

        if (selectedValues.Count == 0)
        {
            _filteringModel.Remove(_activeDescriptorColumnId);
            _activeDescriptorColumnId = _columnId;
            return;
        }

        Func<object, bool>? predicate = _usesCustomValueComparer
            ? item => Contains(selectedValues, _valueAccessor.GetValue(item))
            : null;
        _filteringModel.SetOrUpdate(new FilteringDescriptor(
            columnId: _activeDescriptorColumnId,
            @operator: FilteringOperator.In,
            propertyPath: _propertyPath,
            values: selectedValues,
            predicate: predicate));
    }

    private void ApplySearch()
    {
        Options.Clear();
        string? search = string.IsNullOrWhiteSpace(_searchText) ? null : _searchText.Trim();
        for (int i = 0; i < _allOptions.Count; i++)
        {
            CascadingDistinctValueFilterOption option = _allOptions[i];
            if (search is null || option.Display.Contains(search, StringComparison.CurrentCultureIgnoreCase))
            {
                Options.Add(option);
            }
        }
    }

    private FilteringDescriptor? FindActiveDescriptor()
    {
        IReadOnlyList<FilteringDescriptor> descriptors = _filteringModel.Descriptors;
        for (int i = 0; i < descriptors.Count; i++)
        {
            FilteringDescriptor descriptor = descriptors[i];
            if (Equals(descriptor.ColumnId, _columnId))
            {
                return descriptor;
            }
        }

        if (!string.IsNullOrEmpty(_propertyPath))
        {
            for (int i = 0; i < descriptors.Count; i++)
            {
                FilteringDescriptor descriptor = descriptors[i];
                if (string.Equals(descriptor.PropertyPath, _propertyPath, StringComparison.Ordinal))
                {
                    return descriptor;
                }
            }
        }

        return null;
    }

    private bool Contains(IReadOnlyList<object>? values, object? candidate)
    {
        if (values is null)
        {
            return false;
        }

        for (int i = 0; i < values.Count; i++)
        {
            if (_valueComparer.Equals(values[i], candidate!))
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatDisplayValue(object? value)
    {
        return value is null
            ? "(Empty)"
            : Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class ValueCount
    {
        public ValueCount(object? value, int count = 1)
        {
            Value = value;
            Count = count;
        }

        public object? Value { get; }

        public int Count { get; set; }
    }

    private sealed class NullKeyComparer : IEqualityComparer<object>
    {
        private readonly IEqualityComparer _inner;

        public NullKeyComparer(IEqualityComparer inner)
        {
            _inner = inner;
        }

        bool IEqualityComparer<object>.Equals(object? left, object? right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (ReferenceEquals(left, NullKey) || ReferenceEquals(right, NullKey))
            {
                return false;
            }

            return _inner.Equals(left!, right!);
        }

        int IEqualityComparer<object>.GetHashCode(object value)
        {
            return ReferenceEquals(value, NullKey) ? 0 : _inner.GetHashCode(value);
        }
    }

    private sealed class ActionCommand : ICommand
    {
        private readonly Action _execute;

        public ActionCommand(Action execute)
        {
            _execute = execute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _execute();
    }
}

/// <summary>
/// One compact distinct-value option shown by the experimental filter popup.
/// </summary>
public sealed class CascadingDistinctValueFilterOption : IFilterDistinctValueOption, INotifyPropertyChanged
{
    private readonly Action _selectionChanged;
    private bool _isSelected;

    internal CascadingDistinctValueFilterOption(
        object? value,
        string display,
        int count,
        bool isSelected,
        Action selectionChanged)
    {
        Value = value;
        Display = display;
        Count = count;
        _isSelected = isSelected;
        _selectionChanged = selectionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public object? Value { get; }

    public string Display { get; }

    public int Count { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            _selectionChanged();
        }
    }
}
