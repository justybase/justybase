using CommunityToolkit.Mvvm.ComponentModel;

namespace JustyBase.ViewModels;

public partial class FilterItem(object filterValue, bool isChecked, IValueConverter valueConverter) : ObservableObject
{
    public object _filterValue { get; } = filterValue;
    private readonly IValueConverter _valueConverter = valueConverter;
    private string _stringRepresentation = null;
    private string GetStringRepresentation()
    {
        return _valueConverter.Convert(_filterValue, null, null, null)?.ToString();
    }
    public string FilterText => _stringRepresentation ??= GetStringRepresentation();

    [ObservableProperty]
    public partial bool IsChecked { get; set; } = isChecked;
}
