using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace JustyBase.ViewModels.Tools;

/// <summary>
/// F2: UI-free schema context-menu item.
/// Replaces <c>ObservableCollection&lt;Control&gt;</c> / <c>MenuItem</c> built
/// directly in <c>DbSchemaViewModel</c>. The view renders these via
/// <c>ItemsSource + ItemTemplate</c> (or a converter), the VM never
/// touches Avalonia <c>Control</c>.
/// </summary>
public sealed partial class SchemaMenuItemViewModel : ObservableObject
{
    public SchemaMenuItemViewModel(string header, ICommand? command = null, object? commandParameter = null)
    {
        Header = header;
        Command = command;
        CommandParameter = commandParameter;
    }

    [ObservableProperty]
    public partial string Header { get; set; }

    public ICommand? Command { get; }

    public object? CommandParameter { get; }

    public bool IsSeparator => string.IsNullOrEmpty(Header) && Command is null;

    public ObservableCollection<SchemaMenuItemViewModel> Children { get; } = [];

    public bool HasChildren => Children.Count > 0;

    public static SchemaMenuItemViewModel Separator() => new(string.Empty);
}
