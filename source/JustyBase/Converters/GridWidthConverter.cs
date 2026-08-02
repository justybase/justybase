using System.Globalization;

namespace JustyBase.Converters;

public sealed class GridWidthConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string stringObject)
        {
            if (stringObject == "*")
            {
                return GridLength.Star;
            }
            else
            {
                return GridLength.Parse(stringObject);
            }
        }

        return GridLength.Star;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value.ToString();
    }
}