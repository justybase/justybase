using System.Globalization;

namespace JustyBase.Converters;

/// <summary>
/// Builds the VS Code style tooltip for a search-result file: full path on the
/// first line, last write time and size below. Falls back to the raw path when
/// the file is gone or unreadable.
/// </summary>
public sealed class FileSearchTooltipConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            if (Directory.Exists(path))
            {
                return $"{path}\nModified: {Directory.GetLastWriteTime(path):g} · Folder";
            }

            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return path;
            }

            return $"{path}\nModified: {info.LastWriteTime:g} · Size: {info.Length:N0} bytes";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return path;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
