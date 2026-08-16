using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.Services;
using JustyBase.Services.Schema;
using System.Collections.ObjectModel;

namespace JustyBase.ViewModels.Tools;
public sealed partial class DbSchemaViewModel
{
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly ISimpleLogger _simpleLogger;
    private readonly IClipboardService _clipboardService;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly INetezzaMaintenanceDialogService? _netezzaMaintenanceDialogService;

    public ICommand ContextMenuActionCommand { get; set; }
    public ICommand RefreshTableListCommand { get; set; }
    public ICommand ShowConnectedOnlyCommand { get; set; }

    [ObservableProperty]
    public partial bool SchemaEnabled { get; set; }

    public bool ConnectedOnly
    {
        get;
        set
        {
            SetProperty(ref field, value);
            if (!ConnectedOnly)
            {
                _connectedMenuItem.Header = "Show connected only";
            }
            else
            {
                _connectedMenuItem.Header = "Show all connections";
            }
        }
    }
    private MenuItem _connectedMenuItem;

    [ObservableProperty]
    public partial ObservableCollection<Control> MenuItems { get; set; }
    private ObservableCollection<Control> MenuItemsForConnections { get; set; }
    private ObservableCollection<Control> MenuItemsForSqliteConnections { get; set; }
    private ObservableCollection<Control> MenuItemsForTableGroup { get; set; }
    private ObservableCollection<Control> MenuItemsForTable { get; set; }
    private ObservableCollection<Control> MenuItemsForTablePostgres { get; set; }
    private ObservableCollection<Control> MenuItemsForView { get; set; }
    private ObservableCollection<Control> MenuItemsForViewGroups { get; set; }
    private ObservableCollection<Control> MenuItemsForProcedures { get; set; }
    private ObservableCollection<Control> MenuItemsForFluids { get; set; }
    private ObservableCollection<Control> MenuItemsForProceduresGroups { get; set; }
    private ObservableCollection<Control> MenuItemsForExternalTablesNz { get; set; }
    private ObservableCollection<Control> MenuItemsForExternalTablesNzGroups { get; set; }
    private ObservableCollection<Control> MenuItemsForSynonyms { get; set; }
    private ObservableCollection<Control> MenuItemsForSynonymsGroups { get; set; }
    private ObservableCollection<Control> MenuItemsForSequenceGroups { get; set; }
    private ObservableCollection<Control> MenuItemsForIndexes { get; set; }
    private ObservableCollection<Control> MenuItemsForIndexGroups { get; set; }
    private ObservableCollection<Control> MenuItemsForPartitions { get; set; }
    private ObservableCollection<Control> MenuItemsForPartitionGroups { get; set; }
    private ObservableCollection<Control> MenuItemsForTriggers { get; set; }
    private ObservableCollection<Control> MenuItemsForTriggerGroups { get; set; }
    private ObservableCollection<Control> FallbackMenuItems { get; set; }


    public void PrepareContextMenu(object? data)
    {
        TypeInDatabaseEnum selRowType = GetSelectedType(data);
        switch (selRowType)
        {
            case TypeInDatabaseEnum.otherNoneEntry:
                return;
            case TypeInDatabaseEnum.Connection:
                MenuItems = SelectedSchemaItem?.DatabaseTypeEnumValue == DatabaseTypeEnum.Sqlite
                    ? MenuItemsForSqliteConnections
                    : MenuItemsForConnections;
                break;
            case TypeInDatabaseEnum.baseTables:
                MenuItems = MenuItemsForTableGroup;
                break;
            case TypeInDatabaseEnum.Table:
                MenuItems = SelectedSchemaItem?.DatabaseTypeEnumValue == DatabaseTypeEnum.PostgreSql
                    ? MenuItemsForTablePostgres
                    : MenuItemsForTable;
                break;
            case TypeInDatabaseEnum.View:
                MenuItems = MenuItemsForView;
                break;
            case TypeInDatabaseEnum.baseViews:
                MenuItems = MenuItemsForViewGroups;
                break;
            case TypeInDatabaseEnum.Procedure:
                MenuItems = MenuItemsForProcedures;
                break;
            case TypeInDatabaseEnum.Fluid:
                MenuItems = MenuItemsForFluids;
                break;
            case TypeInDatabaseEnum.baseProcedures:
                MenuItems = MenuItemsForProceduresGroups;
                break;
            case TypeInDatabaseEnum.ExternalTable:
                MenuItems = MenuItemsForExternalTablesNz;
                break;
            case TypeInDatabaseEnum.baseExternals:
                MenuItems = MenuItemsForExternalTablesNzGroups;
                break;
            case TypeInDatabaseEnum.Synonym:
                MenuItems = MenuItemsForSynonyms;
                break;
            case TypeInDatabaseEnum.baseSynonyms:
                MenuItems = MenuItemsForSynonymsGroups;
                break;
            case TypeInDatabaseEnum.baseSequence:
                MenuItems = MenuItemsForSequenceGroups;
                break;
            case TypeInDatabaseEnum.baseIndexes:
                MenuItems = MenuItemsForIndexGroups;
                break;
            case TypeInDatabaseEnum.Index:
                MenuItems = MenuItemsForIndexes;
                break;
            case TypeInDatabaseEnum.basePartitions:
                MenuItems = MenuItemsForPartitionGroups;
                break;
            case TypeInDatabaseEnum.Partition:
                MenuItems = MenuItemsForPartitions;
                break;
            case TypeInDatabaseEnum.baseTriggers:
                MenuItems = MenuItemsForTriggerGroups;
                break;
            case TypeInDatabaseEnum.Trigger:
                MenuItems = MenuItemsForTriggers;
                break;
            default:
                MenuItems = FallbackMenuItems;
                break;
        }
    }

    private ObservableCollection<Control> BuildMenuFromCatalog(TypeInDatabaseEnum type)
    {
        var items = new ObservableCollection<Control>();
        foreach (var entry in SchemaContextMenuCatalog.ForType(type))
        {
            var param = SchemaContextMenuCatalog.GetCommandParameter(entry.Kind, type);
            if (param is null)
            {
                continue;
            }

            items.Add(new MenuItem
            {
                Header = entry.Title,
                Command = ContextMenuActionCommand,
                CommandParameter = param
            });
        }

        return items;
    }

    private void GenerateContextMenu()
    {
        MenuItemsForTable = BuildMenuFromCatalog(TypeInDatabaseEnum.Table);

        MenuItemsForTablePostgres =
        [
            new MenuItem()
            {
                Header = "PostgreSQL",
                ItemsSource = new Control[]
                {
                    new MenuItem() { Header = "Create index template", Command = ContextMenuActionCommand, CommandParameter = "CREATE_INDEX" },
                    new MenuItem() { Header = "Create partition template", Command = ContextMenuActionCommand, CommandParameter = "CREATE_PARTITION" },
                    GetMenuSeparator(),
                    new MenuItem() { Header = "Index/partition overview", Command = ContextMenuActionCommand, CommandParameter = "POSTGRES_INDEX_PARTITION_OVERVIEW" },
                    new MenuItem() { Header = "Maintenance command pack", Command = ContextMenuActionCommand, CommandParameter = "POSTGRES_MAINTENANCE" }
                }
            },
            GetMenuSeparator(),
            .. BuildMenuFromCatalog(TypeInDatabaseEnum.Table)
        ];

        MenuItemsForProceduresGroups =
        [
            new MenuItem() { Header = "Create to new query window", Command = ContextMenuActionCommand, CommandParameter = "CREATE_PROCEDURE" },
            new MenuItem() { Header = "Create all code (ddl) to new query window", Command = ContextMenuActionCommand, CommandParameter = "DDL_ALL_PROCEDURES" },
        ];

        MenuItemsForProcedures =
        [
            new MenuItem() { Header = "Create code (ddl) to new query window", Command = ContextMenuActionCommand, CommandParameter = "DDL_PROCEDURE" },
            new MenuItem() { Header = "Call/Execute to new query window", Command = ContextMenuActionCommand, CommandParameter = "CALL_PROCEDURE" },
        ];

        MenuItemsForFluids =
        [
            new MenuItem() { Header = "Show usage sample to new query window", Command = ContextMenuActionCommand, CommandParameter = "FLUID_SAMPLE" },
        ];

        MenuItemsForView = BuildMenuFromCatalog(TypeInDatabaseEnum.View);

        MenuItemsForViewGroups =
        [
            new MenuItem() { Header = "Create all code (ddl) views", Command = ContextMenuActionCommand, CommandParameter = "DDL_ALL_VIEWS" },
        ];

        MenuItemsForSequenceGroups =
        [
            new MenuItem() { Header = "Create new to query window", Command = ContextMenuActionCommand, CommandParameter = "CREATE_SEQUENCE" },
        ];

        MenuItemsForIndexGroups =
        [
            new MenuItem() { Header = "Create index template", Command = ContextMenuActionCommand, CommandParameter = "CREATE_INDEX" },
            new MenuItem() { Header = "Create all index ddl to new query window", Command = ContextMenuActionCommand, CommandParameter = "DDL_ALL_INDEXES" },
        ];

        MenuItemsForIndexes =
        [
            new MenuItem() { Header = "Create index template", Command = ContextMenuActionCommand, CommandParameter = "CREATE_INDEX" },
            new MenuItem() { Header = "Create index ddl to new query window", Command = ContextMenuActionCommand, CommandParameter = "DDL_INDEX" },
            new MenuItem() { Header = "Create index ddl to clipboard", Command = ContextMenuActionCommand, CommandParameter = "DDL_INDEX_CLIP" },
            new MenuItem() { Header = "Drop index", Command = ContextMenuActionCommand, CommandParameter = "DROP_INDEX" },
        ];

        MenuItemsForTriggerGroups =
        [
            new MenuItem() { Header = "Create all trigger ddl to new query window", Command = ContextMenuActionCommand, CommandParameter = "DDL_ALL_TRIGGERS" },
        ];

        MenuItemsForTriggers =
        [
            new MenuItem() { Header = "Create trigger ddl to new query window", Command = ContextMenuActionCommand, CommandParameter = "DDL_TRIGGER" },
            new MenuItem() { Header = "Create trigger ddl to clipboard", Command = ContextMenuActionCommand, CommandParameter = "DDL_TRIGGER_CLIP" },
            new MenuItem() { Header = "Drop trigger", Command = ContextMenuActionCommand, CommandParameter = "DROP_TRIGGER" },
        ];

        MenuItemsForPartitionGroups =
        [
            new MenuItem() { Header = "Create partition template", Command = ContextMenuActionCommand, CommandParameter = "CREATE_PARTITION" },
            new MenuItem() { Header = "Create all partition ddl to new query window", Command = ContextMenuActionCommand, CommandParameter = "DDL_ALL_PARTITIONS" },
        ];

        MenuItemsForPartitions =
        [
            new MenuItem() { Header = "Create partition template", Command = ContextMenuActionCommand, CommandParameter = "CREATE_PARTITION" },
            new MenuItem() { Header = "Create partition ddl to new query window", Command = ContextMenuActionCommand, CommandParameter = "DDL_PARTITION" },
            new MenuItem() { Header = "Create partition ddl to clipboard", Command = ContextMenuActionCommand, CommandParameter = "DDL_PARTITION_CLIP" },
        ];

        MenuItemsForSynonymsGroups =
        [
            new MenuItem() { Header = "Create to new query window", Command = ContextMenuActionCommand, CommandParameter = "CREATE_SYNONYM" },
            new MenuItem() { Header = "Create all code (ddl) to new query window", Command = ContextMenuActionCommand, CommandParameter = "DDL_ALL_SYNONYMS" },
        ];

        MenuItemsForSynonyms =
        [
            new MenuItem() { Header = "Create code (ddl) to new query window", Command = ContextMenuActionCommand, CommandParameter = "DDL_SYNONYM" },
        ];

        FallbackMenuItems =
        [
            new MenuItem() { Header = "Copy text", Command = ContextMenuActionCommand, CommandParameter = "COPY_TEXT_CLIP" },
        ];

        MenuItemsForConnections = [];
        _connectedMenuItem = new MenuItem() { Header = "Show connected only", Command = ShowConnectedOnlyCommand };
        MenuItemsForConnections.Add(new MenuItem() { Header = "Show/hide header", Command = ShowHideHeadersCommand });
        MenuItemsForConnections.Add(_connectedMenuItem);

        MenuItemsForSqliteConnections =
        [
            new MenuItem() { Header = "SQLite integrity check", Command = ContextMenuActionCommand, CommandParameter = "SQLITE_INTEGRITY_CHECK" },
            new MenuItem() { Header = "SQLite foreign-key check", Command = ContextMenuActionCommand, CommandParameter = "SQLITE_FOREIGN_KEY_CHECK" },
            new MenuItem() { Header = "SQLite database information", Command = ContextMenuActionCommand, CommandParameter = "SQLITE_DATABASE_INFO" },
            GetMenuSeparator(),
            new MenuItem() { Header = "Show/hide header", Command = ShowHideHeadersCommand },
            _connectedMenuItem
        ];

        MenuItemsForTableGroup =
        [
            new MenuItem() { Header = "Create all code (ddl) tables", Command = ContextMenuActionCommand, CommandParameter = "DDL_ALL_TABLES" },
            new MenuItem() { Header = "Recreate all tables", Command = ContextMenuActionCommand, CommandParameter = "RECREATE_ALL_TABLES" },
            new MenuItem() { Header = "Search text in every table", Command = ContextMenuActionCommand, CommandParameter = "SELECT_ALL_SEARCH_TEXT" },
            new MenuItem() { Header = "Search number in every table", Command = ContextMenuActionCommand, CommandParameter = "SELECT_ALL_SEARCH_NUMBER" },
        ];

        MenuItemsForConnections.Add(new MenuItem() { Header = "Refresh table list", Command = RefreshTableListCommand });

        MenuItemsForExternalTablesNz = BuildMenuFromCatalog(TypeInDatabaseEnum.ExternalTable);

        MenuItemsForExternalTablesNzGroups =
        [
            new MenuItem() { Header = "Create code (ddl)", Command = ContextMenuActionCommand, CommandParameter = "DDL_ALL_EXTERNALS" },
        ];

        MenuItems = FallbackMenuItems;
    }

    public void SharedInit()
    {
        ContextMenuActionCommand = new AsyncRelayCommand<string>(ContextMenuActionAsync);
        RefreshTableListCommand = new AsyncRelayCommand(RefreshTableListAsync);
        ShowConnectedOnlyCommand = new RelayCommand(ShowConnectedOnly);
    }
}
