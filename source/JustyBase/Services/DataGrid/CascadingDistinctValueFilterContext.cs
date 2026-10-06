using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Controls.DataGridFiltering;

namespace JustyBase.Services.DataGrid;

/// <summary>
/// Editor state for the results-grid column filter. Edits are kept locally until
/// ApplyCommand succeeds, so closing the popup with Cancel never changes the grid.
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
    private readonly TypeCode? _columnTypeCode;
    private readonly List<CascadingDistinctValueFilterOption> _allOptions = [];

    private object _activeDescriptorColumnId;
    private FilteringDescriptor? _openingDescriptor;
    private string? _searchText;
    private bool _suppressSelectionUpdates;
    private bool _suppressPropertyUpdates;
    private bool _filterEnabled;
    private bool _useValueList = true;
    private FilteringOperator _selectedOperator = FilteringOperator.Contains;
    private string _valueFilterText = string.Empty;
    private string _valueFilterText2 = string.Empty;
    private string? _valueFilterError;
    private bool _selectAllChecked;
    private int _selectedCount;
    private Type? _columnValueType;
    private IReadOnlyList<FilterOperatorChoice> _availableOperators = [];

    public CascadingDistinctValueFilterContext(
        IFilteringModel filteringModel,
        object columnId,
        IDataGridColumnValueAccessor valueAccessor,
        string label,
        string? propertyPath = null,
        IEqualityComparer? valueComparer = null,
        Func<object?, string>? displayFormatter = null,
        TypeCode? columnTypeCode = null)
    {
        _filteringModel = filteringModel ?? throw new ArgumentNullException(nameof(filteringModel));
        _columnId = columnId ?? throw new ArgumentNullException(nameof(columnId));
        _activeDescriptorColumnId = _columnId;
        _valueAccessor = valueAccessor ?? throw new ArgumentNullException(nameof(valueAccessor));
        _usesCustomValueComparer = valueComparer is not null;
        _valueComparer = valueComparer ?? EqualityComparer<object>.Default;
        _displayFormatter = displayFormatter ?? FormatDisplayValue;
        _columnTypeCode = columnTypeCode;
        Label = label ?? string.Empty;
        Options = [];
        ClearAllCommand = new ActionCommand(ClearAll);
        SortAscendingCommand = new ActionCommand(() => SortRequested?.Invoke(ListSortDirection.Ascending));
        SortDescendingCommand = new ActionCommand(() => SortRequested?.Invoke(ListSortDirection.Descending));
        OkCommand = new ActionCommand(ApplyAndClose);
        CancelCommand = new ActionCommand(CancelAndClose);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<ListSortDirection>? SortRequested;

    public event Action? CloseRequested;

    public event Action? RevertRequested;

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

    public ICommand SortAscendingCommand { get; }

    public ICommand SortDescendingCommand { get; }

    public ICommand OkCommand { get; }

    public ICommand CancelCommand { get; }

    public bool FilterEnabled
    {
        get => _filterEnabled;
        set
        {
            if (_filterEnabled == value)
            {
                return;
            }

            _filterEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanApply));
            ValidateValueFilter();
        }
    }

    public bool UseValueList
    {
        get => _useValueList;
        set
        {
            if (_useValueList == value)
            {
                return;
            }

            _useValueList = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UseCondition));
            ValidateValueFilter();
        }
    }

    public bool UseCondition
    {
        get => !UseValueList;
        set => UseValueList = !value;
    }

    public bool SelectAllChecked
    {
        get => _selectAllChecked;
        set
        {
            if (_selectAllChecked == value || _suppressSelectionUpdates)
            {
                return;
            }

            SetVisibleOptionsSelected(value);
        }
    }

    public string SelectionSummary => $"{_selectedCount} of {_allOptions.Count} selected";

    public IReadOnlyList<FilterOperatorChoice> AvailableOperators => _availableOperators;

    public FilterOperatorChoice? SelectedOperatorChoice
    {
        get => _availableOperators.FirstOrDefault(choice => choice.Operator == _selectedOperator);
        set
        {
            if (value is null || value.Operator == _selectedOperator)
            {
                return;
            }

            SelectedOperator = value.Operator;
        }
    }

    public FilteringOperator SelectedOperator
    {
        get => _selectedOperator;
        set
        {
            if (_selectedOperator == value || !_availableOperators.Any(choice => choice.Operator == value))
            {
                return;
            }

            _selectedOperator = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedOperatorChoice));
            OnPropertyChanged(nameof(IsBetween));
            ValidateValueFilter();
        }
    }

    public bool IsBetween => _selectedOperator == FilteringOperator.Between;

    public string ValueFilterText
    {
        get => _valueFilterText;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_valueFilterText, value, StringComparison.Ordinal))
            {
                return;
            }

            _valueFilterText = value;
            OnPropertyChanged();
            OnValueFilterInputChanged();
        }
    }

    public string ValueFilterText2
    {
        get => _valueFilterText2;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_valueFilterText2, value, StringComparison.Ordinal))
            {
                return;
            }

            _valueFilterText2 = value;
            OnPropertyChanged();
            OnValueFilterInputChanged();
        }
    }

    public string ValueFilterError => _valueFilterError ?? string.Empty;

    public bool HasValueFilterError => _valueFilterError is not null;

    public bool CanApply => !FilterEnabled || UseValueList || (HasRequiredValueFilterInput() && !HasValueFilterError);

    /// <summary>
    /// Rebuilds distinct values from the current view. The view already reflects
    /// filters in other columns, so the options cascade as the user narrows results.
    /// </summary>
    public void Refresh(IEnumerable? items, FilteringDescriptor? activeDescriptorOverride = null)
    {
        _openingDescriptor = activeDescriptorOverride ?? FindActiveDescriptor();
        _activeDescriptorColumnId = _openingDescriptor?.ColumnId ?? _columnId;
        _columnValueType = ResolveColumnValueType(items);
        _availableOperators = BuildOperatorChoices(_columnValueType, _columnTypeCode);
        OnPropertyChanged(nameof(AvailableOperators));

        RestoreEditorFromDescriptor(_openingDescriptor);

        var counts = new Dictionary<object, ValueCount>(new NullKeyComparer(_valueComparer));
        if (items is not null)
        {
            foreach (object? item in items)
            {
                if (item is null)
                {
                    continue;
                }

                object? value = _valueAccessor.GetValue(item);
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

        IReadOnlyList<object>? selectedValues = _openingDescriptor?.Operator == FilteringOperator.In
            ? _openingDescriptor.Values
            : null;

        // Retain selections that have disappeared from the current filtered view.
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

        _suppressSelectionUpdates = true;
        try
        {
            _allOptions.Clear();
            _allOptions.AddRange(nextOptions);
            ApplySearch();
        }
        finally
        {
            _suppressSelectionUpdates = false;
        }

        UpdateSelectionSummary();
    }

    private void RestoreEditorFromDescriptor(FilteringDescriptor? descriptor)
    {
        _suppressPropertyUpdates = true;
        try
        {
            _filterEnabled = descriptor is not null;
            _useValueList = descriptor is null || descriptor.Operator == FilteringOperator.In;
            _valueFilterText = string.Empty;
            _valueFilterText2 = string.Empty;
            _valueFilterError = null;

            if (descriptor is not null && descriptor.Operator != FilteringOperator.In)
            {
                _selectedOperator = _availableOperators.Any(choice => choice.Operator == descriptor.Operator)
                    ? descriptor.Operator
                    : _availableOperators.FirstOrDefault()?.Operator ?? FilteringOperator.Contains;

                if (descriptor.Operator == FilteringOperator.Between && descriptor.Values.Count >= 2)
                {
                    _valueFilterText = FormatInputValue(descriptor.Values[0]);
                    _valueFilterText2 = FormatInputValue(descriptor.Values[1]);
                }
                else
                {
                    _valueFilterText = FormatInputValue(descriptor.Value);
                }
            }
            else
            {
                _selectedOperator = _availableOperators.FirstOrDefault()?.Operator ?? FilteringOperator.Contains;
            }
        }
        finally
        {
            _suppressPropertyUpdates = false;
        }

        OnPropertyChanged(nameof(FilterEnabled));
        OnPropertyChanged(nameof(UseValueList));
        OnPropertyChanged(nameof(UseCondition));
        OnPropertyChanged(nameof(SelectedOperator));
        OnPropertyChanged(nameof(SelectedOperatorChoice));
        OnPropertyChanged(nameof(IsBetween));
        OnPropertyChanged(nameof(ValueFilterText));
        OnPropertyChanged(nameof(ValueFilterText2));
        OnPropertyChanged(nameof(ValueFilterError));
        OnPropertyChanged(nameof(HasValueFilterError));
        OnPropertyChanged(nameof(CanApply));
    }

    private void ClearAll()
    {
        _suppressSelectionUpdates = true;
        try
        {
            foreach (CascadingDistinctValueFilterOption option in _allOptions)
            {
                option.IsSelected = false;
            }
        }
        finally
        {
            _suppressSelectionUpdates = false;
        }

        FilterEnabled = false;
        _suppressPropertyUpdates = true;
        try
        {
            _useValueList = true;
            _valueFilterText = string.Empty;
            _valueFilterText2 = string.Empty;
            _valueFilterError = null;
        }
        finally
        {
            _suppressPropertyUpdates = false;
        }

        OnPropertyChanged(nameof(UseValueList));
        OnPropertyChanged(nameof(UseCondition));
        OnPropertyChanged(nameof(ValueFilterText));
        OnPropertyChanged(nameof(ValueFilterText2));
        OnPropertyChanged(nameof(ValueFilterError));
        OnPropertyChanged(nameof(HasValueFilterError));
        OnPropertyChanged(nameof(CanApply));
        UpdateSelectionSummary();
    }

    private void SetVisibleOptionsSelected(bool isSelected)
    {
        _suppressSelectionUpdates = true;
        try
        {
            foreach (IFilterDistinctValueOption option in Options)
            {
                option.IsSelected = isSelected;
            }
        }
        finally
        {
            _suppressSelectionUpdates = false;
        }

        UseValueList = true;
        FilterEnabled = true;
        UpdateSelectionSummary();
    }

    private void OnOptionSelectionChanged()
    {
        if (_suppressSelectionUpdates)
        {
            return;
        }

        UseValueList = true;
        FilterEnabled = true;
        UpdateSelectionSummary();
    }

    private void UpdateSelectionSummary()
    {
        _selectedCount = _allOptions.Count(static option => option.IsSelected);
        bool allVisibleSelected = Options.Count > 0 && Options.All(static option => option.IsSelected);
        if (_selectAllChecked != allVisibleSelected)
        {
            _selectAllChecked = allVisibleSelected;
            OnPropertyChanged(nameof(SelectAllChecked));
        }

        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(CanApply));
    }

    private void ApplySearch()
    {
        Options.Clear();
        string? search = string.IsNullOrWhiteSpace(_searchText) ? null : _searchText.Trim();
        foreach (CascadingDistinctValueFilterOption option in _allOptions)
        {
            if (search is null || option.Display.Contains(search, StringComparison.CurrentCultureIgnoreCase))
            {
                Options.Add(option);
            }
        }

        UpdateSelectionSummary();
    }

    private void OnValueFilterInputChanged()
    {
        if (_suppressPropertyUpdates)
        {
            return;
        }

        UseValueList = false;
        if (HasRequiredValueFilterInput())
        {
            FilterEnabled = true;
        }
        ValidateValueFilter();
    }

    private void ValidateValueFilter()
    {
        string? error = null;
        if (FilterEnabled && UseCondition)
        {
            if (!HasRequiredValueFilterInput())
            {
                error = IsBetween ? "Enter both values for the range." : "Enter a value for this condition.";
            }
            else if (!TryBuildValueDescriptor(out _, out string? parseError))
            {
                error = parseError;
            }
        }

        if (!string.Equals(_valueFilterError, error, StringComparison.Ordinal))
        {
            _valueFilterError = error;
            OnPropertyChanged(nameof(ValueFilterError));
            OnPropertyChanged(nameof(HasValueFilterError));
        }

        OnPropertyChanged(nameof(CanApply));
    }

    private bool HasRequiredValueFilterInput()
    {
        return IsBetween
            ? !string.IsNullOrWhiteSpace(_valueFilterText) && !string.IsNullOrWhiteSpace(_valueFilterText2)
            : !string.IsNullOrWhiteSpace(_valueFilterText);
    }

    private void ApplyAndClose()
    {
        if (!ApplyChanges())
        {
            return;
        }

        CloseRequested?.Invoke();
    }

    private bool ApplyChanges()
    {
        ValidateValueFilter();
        if (!CanApply)
        {
            return false;
        }

        if (!FilterEnabled)
        {
            _filteringModel.Remove(_activeDescriptorColumnId);
            _activeDescriptorColumnId = _columnId;
            return true;
        }

        if (UseValueList)
        {
            var selectedValues = new List<object>();
            foreach (CascadingDistinctValueFilterOption option in _allOptions)
            {
                if (option.IsSelected)
                {
                    // Null is a valid distinct value and the filtering model accepts it
                    // in the same value list as non-null values.
                    selectedValues.Add(option.Value!);
                }
            }

            if (selectedValues.Count == 0)
            {
                _filteringModel.Remove(_activeDescriptorColumnId);
                _activeDescriptorColumnId = _columnId;
                return true;
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
            return true;
        }

        if (!TryBuildValueDescriptor(out FilteringDescriptor? descriptor, out _) || descriptor is null)
        {
            return false;
        }

        _filteringModel.SetOrUpdate(descriptor);
        return true;
    }

    private bool TryBuildValueDescriptor(out FilteringDescriptor? descriptor, out string? error)
    {
        descriptor = null;
        error = null;
        if (!TryConvertInput(_valueFilterText, out object? firstValue))
        {
            error = $"'{_valueFilterText}' is not a valid {_columnValueType?.Name ?? "column"} value.";
            return false;
        }

        if (IsBetween)
        {
            if (!TryConvertInput(_valueFilterText2, out object? secondValue))
            {
                error = $"'{_valueFilterText2}' is not a valid {_columnValueType?.Name ?? "column"} value.";
                return false;
            }

            if (firstValue is not IComparable comparable)
            {
                error = "This column type does not support range comparisons.";
                return false;
            }

            try
            {
                if (comparable.CompareTo(secondValue) > 0)
                {
                    (firstValue, secondValue) = (secondValue, firstValue);
                }
            }
            catch (ArgumentException)
            {
                error = "The range values are not comparable.";
                return false;
            }

            descriptor = new FilteringDescriptor(
                columnId: _activeDescriptorColumnId,
                @operator: FilteringOperator.Between,
                propertyPath: _propertyPath,
                values: new[] { firstValue!, secondValue! });
            return true;
        }

        descriptor = new FilteringDescriptor(
            columnId: _activeDescriptorColumnId,
            @operator: _selectedOperator,
            propertyPath: _propertyPath,
            value: firstValue);
        return true;
    }

    private bool TryConvertInput(string text, out object? value)
    {
        Type targetType = Nullable.GetUnderlyingType(_columnValueType ?? typeof(string)) ?? _columnValueType ?? typeof(string);
        if (targetType == typeof(string) || targetType == typeof(object))
        {
            value = text;
            return true;
        }

        try
        {
            if (targetType == typeof(DateOnly))
            {
                bool parsed = DateOnly.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out DateOnly date);
                value = parsed ? date : null;
                return parsed;
            }

            if (targetType == typeof(TimeOnly))
            {
                bool parsed = TimeOnly.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out TimeOnly time);
                value = parsed ? time : null;
                return parsed;
            }

            // AOT/trim compatible conversion: TypeDescriptor.GetConverter
            // requires unreferenced code (IL2026) and per-type annotations
            // (IL2077). Column values are scalars, so an explicit TryParse
            // cascade covers everything the filter editor can produce.
            if (TryParseScalar(text, targetType, out object? scalar))
            {
                value = scalar;
                return true;
            }

            value = Convert.ChangeType(text, targetType, CultureInfo.CurrentCulture);
            return value is not null;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or ArgumentException or OverflowException or NotSupportedException)
        {
            value = null;
            return false;
        }
    }

    private static bool TryParseScalar(string text, Type targetType, out object? value)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        object? parsed = targetType switch
        {
            _ when targetType == typeof(bool) && bool.TryParse(text, out bool b) => b,
            _ when targetType == typeof(char) && char.TryParse(text, out char c) => c,
            _ when targetType == typeof(byte) && byte.TryParse(text, NumberStyles.Integer, culture, out byte n) => n,
            _ when targetType == typeof(sbyte) && sbyte.TryParse(text, NumberStyles.Integer, culture, out sbyte n) => n,
            _ when targetType == typeof(short) && short.TryParse(text, NumberStyles.Integer, culture, out short n) => n,
            _ when targetType == typeof(ushort) && ushort.TryParse(text, NumberStyles.Integer, culture, out ushort n) => n,
            _ when targetType == typeof(int) && int.TryParse(text, NumberStyles.Integer, culture, out int n) => n,
            _ when targetType == typeof(uint) && uint.TryParse(text, NumberStyles.Integer, culture, out uint n) => n,
            _ when targetType == typeof(long) && long.TryParse(text, NumberStyles.Integer, culture, out long n) => n,
            _ when targetType == typeof(ulong) && ulong.TryParse(text, NumberStyles.Integer, culture, out ulong n) => n,
            _ when targetType == typeof(float) && float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, culture, out float n) => n,
            _ when targetType == typeof(double) && double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, culture, out double n) => n,
            _ when targetType == typeof(decimal) && decimal.TryParse(text, NumberStyles.Number, culture, out decimal n) => n,
            _ when targetType == typeof(DateTime) && DateTime.TryParse(text, culture, DateTimeStyles.None, out DateTime dt) => dt,
            _ when targetType == typeof(DateTimeOffset) && DateTimeOffset.TryParse(text, culture, DateTimeStyles.None, out DateTimeOffset dto) => dto,
            _ when targetType == typeof(TimeSpan) && TimeSpan.TryParse(text, culture, out TimeSpan ts) => ts,
            _ when targetType == typeof(Guid) && Guid.TryParse(text, out Guid g) => g,
            _ when targetType.IsEnum && Enum.TryParse(targetType, text, ignoreCase: true, out object? e) => e,
            _ => null,
        };

        // Distinguish "unsupported type" (null sentinel) from a parsed null:
        // value types never parse to null, so null means no match here.
        value = parsed;
        return parsed is not null;
    }

    private void CancelAndClose()
    {
        RestoreEditorFromDescriptor(_openingDescriptor);
        IReadOnlyList<object>? selectedValues = _openingDescriptor?.Operator == FilteringOperator.In
            ? _openingDescriptor.Values
            : null;
        _suppressSelectionUpdates = true;
        try
        {
            foreach (CascadingDistinctValueFilterOption option in _allOptions)
            {
                option.IsSelected = Contains(selectedValues, option.Value);
            }
        }
        finally
        {
            _suppressSelectionUpdates = false;
        }

        UpdateSelectionSummary();
        RevertRequested?.Invoke();
    }

    private FilteringDescriptor? FindActiveDescriptor()
    {
        IReadOnlyList<FilteringDescriptor> descriptors = _filteringModel.Descriptors;
        for (int i = 0; i < descriptors.Count; i++)
        {
            if (Equals(descriptors[i].ColumnId, _columnId))
            {
                return descriptors[i];
            }
        }

        if (!string.IsNullOrEmpty(_propertyPath))
        {
            for (int i = 0; i < descriptors.Count; i++)
            {
                if (string.Equals(descriptors[i].PropertyPath, _propertyPath, StringComparison.Ordinal))
                {
                    return descriptors[i];
                }
            }
        }

        return null;
    }

    private Type? ResolveColumnValueType(IEnumerable? items)
    {
        Type? fromTypeCode = _columnTypeCode is TypeCode typeCode ? TypeFromTypeCode(typeCode) : null;
        if (fromTypeCode is not null && fromTypeCode != typeof(object))
        {
            return fromTypeCode;
        }

        if (items is not null)
        {
            foreach (object? item in items)
            {
                if (item is not null)
                {
                    object? value = _valueAccessor.GetValue(item);
                    if (value is not null && value is not DBNull)
                    {
                        return value.GetType();
                    }
                }
            }
        }

        return fromTypeCode;
    }

    private static Type? TypeFromTypeCode(TypeCode typeCode) => typeCode switch
    {
        TypeCode.Boolean => typeof(bool),
        TypeCode.Byte => typeof(byte),
        TypeCode.Char => typeof(char),
        TypeCode.DateTime => typeof(DateTime),
        TypeCode.Decimal => typeof(decimal),
        TypeCode.Double => typeof(double),
        TypeCode.Int16 => typeof(short),
        TypeCode.Int32 => typeof(int),
        TypeCode.Int64 => typeof(long),
        TypeCode.SByte => typeof(sbyte),
        TypeCode.Single => typeof(float),
        TypeCode.String => typeof(string),
        TypeCode.UInt16 => typeof(ushort),
        TypeCode.UInt32 => typeof(uint),
        TypeCode.UInt64 => typeof(ulong),
        _ => null
    };

    private static IReadOnlyList<FilterOperatorChoice> BuildOperatorChoices(Type? valueType, TypeCode? typeCode)
    {
        Type effectiveType = valueType ?? TypeFromTypeCode(typeCode ?? TypeCode.Object) ?? typeof(string);
        var names = new List<(string Name, string Label)>();
        AddAvailableOperator(names, "Equals", "Equals", "Equal");
        AddAvailableOperator(names, "Not equals", "NotEquals", "NotEqual");

        if (effectiveType == typeof(string) || effectiveType == typeof(char))
        {
            AddAvailableOperator(names, "Contains", "Contains");
            AddAvailableOperator(names, "Does not contain", "NotContains");
            AddAvailableOperator(names, "Starts with", "StartsWith");
            AddAvailableOperator(names, "Ends with", "EndsWith");
        }
        else if (IsNumericType(effectiveType) || effectiveType == typeof(DateTime) || effectiveType == typeof(DateOnly) || effectiveType == typeof(TimeOnly))
        {
            AddAvailableOperator(names, "Greater than", "GreaterThan");
            AddAvailableOperator(names, "Greater than or equal", "GreaterThanOrEqual");
            AddAvailableOperator(names, "Less than", "LessThan");
            AddAvailableOperator(names, "Less than or equal", "LessThanOrEqual");
            AddAvailableOperator(names, "Between", "Between");
        }

        var result = new List<FilterOperatorChoice>(names.Count);
        foreach ((string label, string enumName) in names)
        {
            if (Enum.TryParse(enumName, ignoreCase: true, out FilteringOperator op) && Enum.IsDefined(op))
            {
                result.Add(new FilterOperatorChoice(op, label));
            }
        }

        return result;
    }

    private static void AddAvailableOperator(List<(string Name, string Label)> names, string label, params string[] enumNames)
    {
        foreach (string enumName in enumNames)
        {
            if (Enum.TryParse(enumName, ignoreCase: true, out FilteringOperator op) && Enum.IsDefined(op))
            {
                names.Add((label, enumName));
                return;
            }
        }
    }

    private static bool IsNumericType(Type type)
    {
        TypeCode typeCode = Type.GetTypeCode(type);
        return typeCode is TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16
            or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64
            or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;
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

    private static string FormatDisplayValue(object? value) => value is null or DBNull
        ? "(Empty)"
        : Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;

    private static string FormatInputValue(object? value) => value is null or DBNull
        ? string.Empty
        : Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class ValueCount(object? value, int count = 1)
    {
        public object? Value { get; } = value;
        public int Count { get; set; } = count;
    }

    private sealed class NullKeyComparer(IEqualityComparer inner) : IEqualityComparer<object>
    {
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

            return inner.Equals(left!, right!);
        }

        int IEqualityComparer<object>.GetHashCode(object value) =>
            ReferenceEquals(value, NullKey) ? 0 : inner.GetHashCode(value);
    }

    private sealed class ActionCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }
}

public sealed record FilterOperatorChoice(FilteringOperator Operator, string Label);

/// <summary>A distinct value and its current-view count shown by the filter popup.</summary>
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
