using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.Services.Schema;
using JustyBase.ViewModels.Documents;
using System.Collections.ObjectModel;

namespace JustyBase.ViewModels;

public sealed class DbObjectQuickMenuViewModel : ObservableObject
{
    public string ObjectTitle { get; set; } = "some title..";
    public ObservableCollection<QuickMenuItem> OptionsList { get; set; }
    public Action CloseAction { get; init; }

    public SqlDocumentViewModel SqlDocVM
    {
        get;
        set
        {
            field = value;
            DatabaseTypeEnum databaseType = value.CurrentDatabaseType;
            DatabaseCapabilities capabilities = DatabaseCapabilitiesCatalog.For(databaseType);

            var items = new List<QuickMenuItem>
            {
                new("Jump to [F6]", 12, SqlDocVM.JumpToSelectedItem),
                new("Select [F7]", 1, SqlDocVM.SelectSelectedItem),
            };

            if (capabilities.SupportsDropObject)
            {
                items.Add(new QuickMenuItem("Drop", 12, SqlDocVM.DropSelectedItem));
            }
            if (capabilities.SupportsRenameTable)
            {
                items.Add(new QuickMenuItem("Rename", 12, SqlDocVM.RenameSelectedItem));
            }
            if (capabilities.SupportsGroomTable)
            {
                items.Add(new QuickMenuItem(
                    SchemaContextMenuCatalog.ResolveTitle("GROOM", "Groom", databaseType),
                    6,
                    SqlDocVM.GroomSelectedItem));
            }
            if (capabilities.SupportsRecreateTable)
            {
                items.Add(new QuickMenuItem("Recreate", 10, SqlDocVM.RecreateSelectedItem));
            }
            if (capabilities.SupportsDdl)
            {
                items.Add(new QuickMenuItem("DDL", 10, SqlDocVM.DdlSelectedItem));
            }
            if (capabilities.SupportsTop100Select)
            {
                items.Add(new QuickMenuItem("Top 100", 3, SqlDocVM.SelectSelectedItem));
            }
            if (capabilities.SupportsTableStatistics)
            {
                items.Add(new QuickMenuItem(
                    SchemaContextMenuCatalog.ResolveTitle("STATS", "Generate statistics", databaseType),
                    7,
                    SqlDocVM.GenerateStatsSelectedItem));
            }
            if (capabilities.SupportsCreateTableFrom)
            {
                items.Add(new QuickMenuItem("Create from", 6, SqlDocVM.CreateFromSelectedItem));
            }

            OptionsList = new ObservableCollection<QuickMenuItem>(items);
        }
    }
}
public class QuickMenuItem(string title, int number, Func<Task> action)
{
    public string Title { get; set; } = title;
    public int Number { get; set; } = number;
    public ICommand DoJob { get; set; } = new AsyncRelayCommand(action);
}
