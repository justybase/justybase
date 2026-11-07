using Avalonia.Controls;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using System.Collections.ObjectModel;

namespace JustyBase.Services.Schema;

/// <summary>
/// Adapts the shared, per-object-type schema menu definitions to a specific
/// engine. The catalog in <see cref="SchemaContextMenuCatalog"/> lists every
/// action the host knows how to run; this filter removes the ones the active
/// engine cannot execute with valid SQL (for example Netezza GROOM and
/// distribution actions on Db2) and renames actions whose SQL meaning changes
/// per engine.
/// </summary>
/// <remarks>
/// Lives outside the ViewModels folder because it clones Avalonia menu items;
/// the MVVM separation guard counts Avalonia control references in view models.
/// </remarks>
public static class SchemaContextMenuFilter
{
    /// <summary>
    /// Clones <paramref name="source"/> keeping only actions supported by
    /// <paramref name="capabilities"/>. Cloning keeps the shared per-type
    /// collections reusable across engines (and concurrent context menus).
    /// </summary>
    public static ObservableCollection<Control> Filter(
        ObservableCollection<Control>? source,
        DatabaseCapabilities capabilities,
        DatabaseTypeEnum databaseType)
    {
        var filtered = new ObservableCollection<Control>();
        if (source is null)
        {
            return filtered;
        }

        foreach (var control in source)
        {
            if (control is not MenuItem menuItem)
            {
                continue;
            }

            if (Equals(menuItem.Header, "-"))
            {
                filtered.Add(new MenuItem { Header = "-" });
                continue;
            }

            string? commandParameter = menuItem.CommandParameter as string;
            if (!SchemaContextMenuCatalog.IsActionEnabled(commandParameter, capabilities))
            {
                continue;
            }

            bool hadChildren = menuItem.Items.Count > 0 || menuItem.ItemsSource is IEnumerable<Control>;

            var clone = new MenuItem
            {
                Header = SchemaContextMenuCatalog.ResolveTitle(
                    commandParameter,
                    menuItem.Header?.ToString() ?? string.Empty,
                    databaseType),
                Command = menuItem.Command,
                CommandParameter = menuItem.CommandParameter,
                IsEnabled = menuItem.IsEnabled,
            };

            foreach (var child in EnumerateChildMenuItems(menuItem))
            {
                string? childParameter = child.CommandParameter as string;
                if (!SchemaContextMenuCatalog.IsActionEnabled(childParameter, capabilities))
                {
                    continue;
                }

                clone.Items.Add(new MenuItem
                {
                    Header = SchemaContextMenuCatalog.ResolveTitle(
                        childParameter,
                        child.Header?.ToString() ?? string.Empty,
                        databaseType),
                    Command = child.Command,
                    CommandParameter = child.CommandParameter,
                    IsEnabled = child.IsEnabled,
                });
            }

            // Drop submenus whose every action was filtered out.
            if (hadChildren && clone.Items.Count == 0)
            {
                continue;
            }

            filtered.Add(clone);
        }

        return filtered;
    }

    private static IEnumerable<MenuItem> EnumerateChildMenuItems(MenuItem menuItem)
    {
        foreach (var item in menuItem.Items.OfType<MenuItem>())
        {
            yield return item;
        }

        if (menuItem.Items.Count == 0 && menuItem.ItemsSource is IEnumerable<Control> sourceItems)
        {
            foreach (var item in sourceItems.OfType<MenuItem>())
            {
                yield return item;
            }
        }
    }
}
