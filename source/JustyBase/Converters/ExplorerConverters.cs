using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace JustyBase.Converters;

/// <summary>
/// Folder icon visibility for the Explorer tree: values are
/// [IsDirectory, IsExpanded], parameter "open" or "closed".
/// </summary>
public sealed class DirectoryOpenToVisibleConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isDirectory = values.Count > 0 && values[0] is true;
        bool isExpanded = values.Count > 1 && values[1] is true;
        return string.Equals(parameter as string, "open", StringComparison.OrdinalIgnoreCase)
            ? isDirectory && isExpanded
            : isDirectory && !isExpanded;
    }
}

/// <summary>
/// Explorer row selection fill: values are [nodePath, selectedPath].
/// Translucent blue when they match, transparent otherwise.
/// </summary>
public sealed class ExplorerSelectionConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush SelectedFill =
        new(Color.FromArgb(0x42, 0x2D, 0x6F, 0xC8));

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        string? nodePath = values.Count > 0 ? values[0] as string : null;
        string? selectedPath = values.Count > 1 ? values[1] as string : null;
        return !string.IsNullOrEmpty(nodePath)
            && string.Equals(nodePath, selectedPath, StringComparison.OrdinalIgnoreCase)
            ? SelectedFill
            : Brushes.Transparent;
    }
}
