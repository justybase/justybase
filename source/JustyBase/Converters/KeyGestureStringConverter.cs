using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;

namespace JustyBase.Converters;

/// <summary>
/// Turns a shortcut string from the keymap ("Ctrl+Shift+O") into the <see cref="KeyGesture"/> that
/// <c>MenuItem.InputGesture</c> expects. Keeping gestures as strings in the view models is deliberate:
/// the MVVM layer must not reference Avalonia input types.
/// </summary>
public sealed class KeyGestureStringConverter : IValueConverter
{
    public static KeyGestureStringConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return KeyGesture.Parse(text);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
