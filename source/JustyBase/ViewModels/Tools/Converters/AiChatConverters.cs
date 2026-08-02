using System.Globalization;
using JustyBase.Common.Models;

namespace JustyBase.ViewModels.Tools.Converters;

public sealed class ModeToBoolConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly ModeToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ChatMode currentMode && parameter is string modeSlug)
        {
            return currentMode == ChatModeExtensions.FromSlug(modeSlug);
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string modeSlug)
        {
            return ChatModeExtensions.FromSlug(modeSlug);
        }
        return Avalonia.AvaloniaProperty.UnsetValue;
    }
}

public sealed class BoolToColorConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly BoolToColorConverter Instance = new();

    private static readonly ISolidColorBrush OrangeBrush = new SolidColorBrush(Avalonia.Media.Colors.Orange);
    private static readonly ISolidColorBrush GrayBrush = new SolidColorBrush(Avalonia.Media.Colors.Gray);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
            return OrangeBrush;
        return GrayBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Avalonia.AvaloniaProperty.UnsetValue;
    }
}

public sealed class BoolToSuccessColorConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly BoolToSuccessColorConverter Instance = new();

    private static readonly ISolidColorBrush LimeGreenBrush = new SolidColorBrush(Avalonia.Media.Colors.LimeGreen);
    private static readonly ISolidColorBrush OrangeRedBrush = new SolidColorBrush(Avalonia.Media.Colors.OrangeRed);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
            return LimeGreenBrush;
        return OrangeRedBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Avalonia.AvaloniaProperty.UnsetValue;
    }
}
