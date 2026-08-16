using System.Globalization;

namespace JustyBase.Converters;

public class BoolToConnectionColorConverter : IValueConverter
{
    public static readonly BoolToConnectionColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isConnected)
        {
            return isConnected ? Colors.Green : Colors.Red;
        }
        return Colors.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StringToVisibilityConverter : IValueConverter
{
    public static readonly StringToVisibilityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str && !string.IsNullOrWhiteSpace(str))
        {
            return true;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class RoleToAlignmentConverter : IValueConverter
{
    public static readonly RoleToAlignmentConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string role)
        {
            return string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left;
        }
        return HorizontalAlignment.Left;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class RoleToBrushConverter : IValueConverter
{
    public static readonly RoleToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string role)
        {
            bool isUser = string.Equals(role, "user", StringComparison.OrdinalIgnoreCase);
            
            // Use theme-aware colors
            if (isUser)
            {
                // User messages - blue accent
                return new SolidColorBrush(Color.Parse("#0078D4"));
            }
            else
            {
                // Assistant messages - use theme background with slight tint
                // For dark theme: lighter gray, for light theme: white/light gray
                return new SolidColorBrush(Color.Parse("#F0F0F0"));
            }
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class RoleToForegroundConverter : IValueConverter
{
    public static readonly RoleToForegroundConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string role)
        {
            bool isUser = string.Equals(role, "user", StringComparison.OrdinalIgnoreCase);
            
            // User messages - white text on blue
            // Assistant messages - black/dark text on light background
            return isUser ? new SolidColorBrush(Colors.White) : new SolidColorBrush(Colors.Black);
        }
        return new SolidColorBrush(Colors.Black);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StreamingStatusConverter : IValueConverter
{
    public static readonly StreamingStatusConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
        {
            return "Thinking...";
        }
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
