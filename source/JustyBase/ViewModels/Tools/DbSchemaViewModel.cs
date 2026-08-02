using Avalonia.Controls.DataGridHierarchical;
using Avalonia.Data;
using Avalonia.Data.Core;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using JustyBase.Common.Contracts;
using JustyBase.Converters;
using JustyBase.Helpers;
using JustyBase.NetezzaDdl;
using JustyBase.Models.Tools;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginDatabaseBase.Database;
using JustyBase.Services;
using JustyBase.Services.Database;
using JustyBase.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace JustyBase.ViewModels.Tools;
public sealed partial class DbSchemaViewModel : Tool
{
    public HierarchicalModel<DbSchemaModel> SchemaModel { get; }
    public ObservableCollection<DataGridColumnDefinition> ColumnDefinitions { get; }

    [ObservableProperty]
    public partial DbSchemaModel? SelectedSchemaItem { get; set; }

    private readonly SemaphoreSlim _revealGate = new(1, 1);

    private TypeInDatabaseEnum GetSelectedType(object data) // evo..
    {
        if (SelectedSchemaItem is null)
        {
            MenuItems = FallbackMenuItems;
            return TypeInDatabaseEnum.otherNoneEntry;
        }
        return SelectedSchemaItem.ActualTypeInDatabase;
    }

    public Action FocusAndBringSelectionIntoView;

    public async Task ExpandToNodeFull(string[] toExpandPath, CancellationToken cancellationToken = default)
    {
        if (toExpandPath is null || toExpandPath.Length == 0)
        {
            return;
        }

        bool gateEntered = false;

        try
        {
            await _revealGate.WaitAsync(cancellationToken);
            gateEntered = true;

            cancellationToken.ThrowIfCancellationRequested();
            ShowThis();

            DbSchemaModel? current = null;
            IEnumerable<DbSchemaModel> level = _connectionCollection;

            for (int i = 0; i < toExpandPath.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (i > 0)
                {
                    if (current is null)
                        return;

                    // The database load itself is shared by the node and may not
                    // support cancellation. Wait for it cancellably so a newer
                    // reveal can become active without applying stale UI changes.
                    await current.LoadChildrenAsync().WaitAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    level = current.Children;
                }

                current = null;
                foreach (var item in level)
                {
                    if (item.Name == toExpandPath[i])
                    {
                        current = item;
                        break;
                    }
                }

                if (current is null)
                    return;

                if (i < toExpandPath.Length - 1)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    current.IsExpanded = true;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            SchemaModel?.Refresh();

            if (current is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SelectedSchemaItem = current;
                await Dispatcher.UIThread.InvokeAsync(
                    () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        FocusAndBringSelectionIntoView?.Invoke();
                    },
                    DispatcherPriority.Loaded);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer reveal superseded this request. Do not log or surface it.
        }
        catch (Exception ex)
        {
            _simpleLogger?.TrackError(ex, isCrash: false);
        }
        finally
        {
            if (gateEntered)
            {
                _revealGate.Release();
            }
        }
    }

    private DbSchemaModel LastItemConrtextMenuReq => SelectedSchemaItem ?? _connectionCollection.FirstOrDefault();

    public void ShowThis()
    {
        try
        {
            if (this.Owner is ToolDock toolDock)
            {
                toolDock.ActiveDockable = this;
            }
        }
        catch (Exception ex)
        {
            _simpleLogger?.TrackError(ex, isCrash: false);
        }
    }

    private MenuItem GetMenuSeparator() => new() { Header = "-" };

    private void ShowConnectedOnly()
    {
        if (!ConnectedOnly)
        {
            for (int i = 0; i < _connectionCollection.Count; i++)
            {
                if (!DatabaseServiceHelpers.IsDatabaseConnected(_connectionCollection[i].Name))
                {
                    _connectionCollection.RemoveAt(i);
                    i--;
                }
            }
            ConnectedOnly = true;
        }
        else
        {
            IntitSchema(skipConnected: true);
            ConnectedOnly = false;
        }
    }
    private async Task RefreshTableListAsync()
    {
        var selectedItem = SelectedSchemaItem;
        if (selectedItem is null) return;
        
        bool wasExpanded = selectedItem.IsExpanded;
        selectedItem.IsExpanded = false;
        SchemaEnabled = false;
        selectedItem.ClearChildren();
        _ = await Task.Run(() => DatabaseServiceHelpers.GetDatabaseService(_generalApplicationData, selectedItem.ConnectionName, forceRefresh: true));
        SchemaEnabled = true;
        selectedItem.IsExpanded = wasExpanded;
    }

    private readonly ObservableCollection<DbSchemaModel> _connectionCollection = [];


    [ObservableProperty]
    public partial bool ShowHeader { get; set; } = false;
    public ICommand ShowHideHeadersCommand { get; set; }


    public DbSchemaViewModel(Dock.Model.Core.IFactory factory, IClipboardService clipboard, IGeneralApplicationData generalApplicationData,
        ISimpleLogger simpleLogger, IMessageForUserTools messageForUserTools,
        INetezzaMaintenanceDialogService? netezzaMaintenanceDialogService = null)
    {
        _clipboardService = clipboard;
        _generalApplicationData = generalApplicationData;
        _simpleLogger = simpleLogger;
        this.Factory = factory;
        _messageForUserTools = messageForUserTools;
        _netezzaMaintenanceDialogService = netezzaMaintenanceDialogService;

        SharedInit();
        ShowHideHeadersCommand = new RelayCommand(() => ShowHeader = !ShowHeader);
        SchemaEnabled = true;

        GenerateContextMenu();

        IntitSchema();

        var options = new HierarchicalOptions<DbSchemaModel>
        {
            ChildrenSelector = item => item.Children,
            IsExpandedSelector = item => item.IsExpanded,
            IsExpandedSetter = (item, value) => item.IsExpanded = value,
            IsLeafSelector = item => !item.IsExpandedable
        };

        SchemaModel = new HierarchicalModel<DbSchemaModel>(options);
        SchemaModel.SetRoots(_connectionCollection);

        var nameColumn = new DataGridHierarchicalColumnDefinition
        {
            Header = "Name",
            Binding = CreateNodeBinding<DbSchemaModel>("Item", item => item),
            CellTemplateKey = "DbSchemaNameTemplate",
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        };

        var infoColumn = new DataGridTextColumnDefinition
        {
            Header = "Info",
            Binding = CreateNodeBinding<string>("Info", item => item.Info),
            Width = new DataGridLength(150, DataGridLengthUnitType.Pixel)
        };

        ColumnDefinitions = new ObservableCollection<DataGridColumnDefinition>
        {
            nameColumn,
            infoColumn
        };
    }

    private async Task ContextMenuActionAsync(string optionName)
    {
        if (LastItemConrtextMenuReq is null)
        {
            return;
        }

        string CONNECTION_NAME = LastItemConrtextMenuReq.ConnectionName;

        if (CONNECTION_NAME is null)
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance("CONNECTION_NAME is null", "Error");
        }

        if (optionName == "DISTRIBUTE_CHART_NZ")
        {
            await ShowDistributionChartAsync(CONNECTION_NAME);
            return;
        }

        if ((optionName is "GROOM" or "STATS") && _netezzaMaintenanceDialogService is not null)
        {
            var item = LastItemConrtextMenuReq;
            var qualified = NetezzaMaintenanceSql.Qualify(item.Database, item.CurrentSchema, item.Name);
            var kind = optionName == "GROOM"
                ? NetezzaMaintenanceDialogKind.Groom
                : NetezzaMaintenanceDialogKind.GenerateStats;
            var wizardSql = await _netezzaMaintenanceDialogService.ShowAsync(kind, qualified);
            if (string.IsNullOrWhiteSpace(wizardSql))
            {
                return;
            }

            ((IActiveDocumentManager)Factory).AddNewDocument("");
            ((IActiveDocumentManager)Factory).InsertSnippetTextToActiveDocument(wizardSql, CONNECTION_NAME);
            return;
        }

        if (optionName is "IMPORT_DATA")
        {
            var item = LastItemConrtextMenuReq;
            ((IActiveDocumentManager)Factory).OpenImportDocument(
                CONNECTION_NAME,
                item.Database,
                item.CurrentSchema,
                item.Name);
            return;
        }

        var sql = await IDatabaseSchemaItem.GetCode(LastItemConrtextMenuReq, CONNECTION_NAME, optionName, _generalApplicationData, _simpleLogger);

        if (optionName.EndsWith("CLIP"))
        {
            await _clipboardService.SetTextAsync(sql);
        }
        else
        {
            ((IActiveDocumentManager)Factory).AddNewDocument("");
            ((IActiveDocumentManager)Factory).InsertSnippetTextToActiveDocument(sql, CONNECTION_NAME);
        }
    }

    private async Task ShowDistributionChartAsync(string connectionName)
    {
        var item = LastItemConrtextMenuReq;
        if (item is null)
        {
            return;
        }

        try
        {
            var confirm = await _messageForUserTools.ShowConfirmationDialogAsync(
                $"Compute distribution skew for {item.Database}.{item.CurrentSchema}.{item.Name}?\n\nThis runs COUNT(*) GROUP BY datasliceid and may be expensive on large tables.",
                "Distribution chart");
            if (!confirm)
            {
                return;
            }

            var skewService = Program.ServiceProvider?.GetService<NetezzaSessionMonitorService>();
            if (skewService is null)
            {
                _messageForUserTools.ShowSimpleMessageBoxInstance("Session/skew service is not available.", "Error");
                return;
            }

            var result = await skewService.GetTableSkewAsync(
                connectionName,
                item.Database,
                item.CurrentSchema,
                item.Name);

            _messageForUserTools.DispatcherActionInstance(async () =>
            {
                var vm = new NetezzaDistributionChartViewModel(result);
                var window = new global::JustyBase.Views.OtherDialogs.NetezzaDistributionChartWindow(vm);
                var helpers = Program.ServiceProvider?.GetService<IAvaloniaSpecificHelpers>();
                if (helpers is not null)
                {
                    await window.ShowDialog(helpers.GetMainWindow());
                }
                else
                {
                    window.Show();
                }
            });
        }
        catch (Exception ex)
        {
            _simpleLogger.TrackError(ex, isCrash: false);
            _messageForUserTools.ShowSimpleMessageBoxInstance(ex.Message, "Distribution chart");
        }
    }


    //private void SchemaSource_RowCollapsed(object? sender, RowEventArgs<HierarchicalRow<DbSchemaModel>> e)
    //{
    //    SchemaSource.Columns.SetColumnWidth(0, new GridLength(80, GridUnitType.Pixel));
    //    SchemaSource.Columns.SetColumnWidth(0, new GridLength(80, GridUnitType.Auto));
    //}

    private void IntitSchema(bool skipConnected = false)
    {
        ConnectedOnly = false;
        foreach (var item in _generalApplicationData.LoginDataDic)
        {
            if (skipConnected)
            {
                if (DatabaseServiceHelpers.IsDatabaseConnected(item.Value.ConnectionName))
                {
                    continue;
                }
            }

            if (_connectionCollection.Select(a => a.Name).Contains(item.Key))
            {
                continue;
            }

            DatabaseTypeEnum dbType = DatabaseServiceHelpers.StringToDatabaseTypeEnum(item.Value.Driver);

            _connectionCollection.Add(new DbSchemaModel(TypeInDatabaseEnum.Connection, dbType, _generalApplicationData)
            {
                Name = item.Key,
                Info = "connection",
                ConnectionName = item.Key
            });
        }
    }

    //reset after added/deleted new
    public void ResedConnectionList()
    {
        _connectionCollection.Clear();
        foreach (var item in _generalApplicationData.LoginDataDic)
        {
            if (ConnectedOnly)
            {
                if (DatabaseServiceHelpers.IsDatabaseConnected(item.Value.ConnectionName))
                {
                    continue;
                }
            }
            DatabaseTypeEnum dbType = DatabaseServiceHelpers.StringToDatabaseTypeEnum(item.Value.Driver);
            if (!_connectionCollection.Select(a => a.Name).Contains(item.Key))
            {
                _connectionCollection.Add(new DbSchemaModel(TypeInDatabaseEnum.Connection, dbType, _generalApplicationData)
                {
                    Name = item.Key,
                    Info = "connection",
                    ConnectionName = item.Key
                });
            }
        }
        OnPropertyChanged(nameof(_connectionCollection));
        OnPropertyChanged(nameof(SchemaModel));
    }

    private Control DbItemTemplate(DbSchemaModel node, INameScope ns)
    {
        var target = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                    new Image
                    {
                        [!Image.SourceProperty] =
                        //new Binding(nameof(node.TypeInDatabase))
                        new Binding(nameof(node.Self))
                        {
                            Converter = App.Current.Resources["databaseIconConverter"] as DatabaseIconConverter
                        },
                        //Source = SelectBitmap(node),
                        Margin = new Thickness(0, 0, 4, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Stretch = Avalonia.Media.Stretch.None
                    },
                    new Border()
                    {
                        Child =
                        new TextBlock
                        {
                            [!TextBlock.TextProperty] = new Binding(nameof(DbSchemaModel.Name)),
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                        [!ToolTip.TipProperty] = new Binding(nameof(DbSchemaModel.ToolTipText))
                    }
                }
        };

        return target;
    }
    //private Control DbEditItemTemplate(DbSchemaModel node, INameScope ns)
    //{
    //    if (node.ActualTypeInDatabase != TypeInDatabaseEnum.ColumnComment || node.DatabaseTypeEnumValue != DatabaseTypeEnum.NetezzaSQL || node.DatabaseTypeEnumValue != DatabaseTypeEnum.NetezzaSQLOdbc)
    //    {
    //        return DbItemTemplate(node, ns);
    //    }

    //    var target = new TextBox
    //    {
    //        [!TextBox.TextProperty] = new Binding(nameof(DbSchemaModel.Name)),
    //        VerticalAlignment = VerticalAlignment.Center,
    //        HorizontalAlignment= HorizontalAlignment.Center,
    //        Padding = new Thickness(0.0),
    //        Margin = new Thickness(3,1),
    //        Height = 24,
    //        MinHeight = 24,
    //        FontSize= 12
    //    };

    //    return target;
    //}

    private static DataGridBindingDefinition CreateNodeBinding<TValue>(string name, Func<DbSchemaModel, TValue> getter)
    {
        return CreateBinding<HierarchicalNode, TValue>(
            name,
            node => getter((DbSchemaModel)node.Item));
    }

    private static DataGridBindingDefinition CreateBinding<TItem, TValue>(
        string name,
        Func<TItem, TValue> getter,
        Action<TItem, TValue>? setter = null)
    {
        var propertyInfo = new ClrPropertyInfo(
            name,
            target => TryGetValue(target, getter),
            setter == null
                ? null
                : (target, value) => TrySetValue(target, value, setter),
            typeof(TValue));

        return DataGridBindingDefinition.Create<TItem, TValue>(propertyInfo, getter, setter);
    }

    private static TValue TryGetValue<TItem, TValue>(object target, Func<TItem, TValue> getter)
    {
        if (target is not TItem item)
        {
            return default!;
        }
        return getter(item);
    }

    private static void TrySetValue<TItem, TValue>(object target, object? value, Action<TItem, TValue> setter)
    {
        if (target is not TItem item)
        {
            return;
        }
        if (value is null)
        {
            setter(item, default!);
            return;
        }
        if (value is TValue typedValue)
        {
            setter(item, typedValue);
            return;
        }
        setter(item, (TValue)value);
    }



}
