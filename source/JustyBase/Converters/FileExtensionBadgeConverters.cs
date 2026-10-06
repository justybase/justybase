using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using JustyBase.Services.FileExplorer;

namespace JustyBase.Converters;

/// <summary>
/// VS Code style badge: Path -&gt; short glyph ("SQL", "JS", "{}"...).
/// Empty string when the extension is unknown (view falls back to generic icon).
/// </summary>
public sealed class FileExtensionGlyphConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => FileExtensionStyle.GetGlyph(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Path -&gt; badge foreground brush (theme-independent hex, works in light+dark).</summary>
public sealed class FileExtensionBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string hex = FileExtensionStyle.GetColorHex(value as string);
        try
        {
            return new SolidColorBrush(Color.Parse(hex));
        }
        catch (FormatException)
        {
            return Brushes.Gray;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Path -&gt; true when a badge glyph exists (to hide the generic file icon).</summary>
public sealed class FileExtensionHasBadgeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => FileExtensionStyle.HasBadge(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// File icon visibility: values are [isFile, hasBadge], parameter "badge" or "generic".
/// "badge" =&gt; file with known extension, "generic" =&gt; file with unknown extension.
/// </summary>
public sealed class FileBadgeVisibilityConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isFile = values.Count > 0 && values[0] is true;
        bool hasBadge = values.Count > 1 && values[1] is true;
        return string.Equals(parameter as string, "badge", StringComparison.OrdinalIgnoreCase)
            ? isFile && hasBadge
            : isFile && !hasBadge;
    }
}
