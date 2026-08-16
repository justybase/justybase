using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Models;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginDatabaseBase.Database;
using System.Collections.ObjectModel;

namespace JustyBase.ViewModels.Tools;

public sealed partial class SchemaSearchViewModel : Tool, IDisposable
{
    /// <summary>
    /// DataGrid grouping is disabled for scroll performance (same idea as SQL Results:
    /// uniform rows + logical scroll). Items are still sorted by Type then Name.
    /// </summary>
    private static void SortSchemaItemsByTypeAndName(List<SchemaSearchItem> items)
    {
        items.Sort(static (a, b) =>
        {
            int typeOrder = string.Compare(a.Type, b.Type, StringComparison.OrdinalIgnoreCase);
            if (typeOrder != 0)
            {
                return typeOrder;
            }

            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static ObservableCollection<SchemaSearchItem> ToDisplayCollection(IReadOnlyList<SchemaSearchItem> items)
    {
        var bulk = new BulkObservableCollection<SchemaSearchItem>();
        bulk.AddRange(items);
        return bulk;
    }

    /// <summary>ProDataGrid fast-path: bind the collection directly (no DataGridCollectionView grouping).</summary>
    private void PublishGridItems(ObservableCollection<SchemaSearchItem> items)
    {
        SelectedSearchItem = null;
        SchemaSearchItemCollections = items;
        SchemaSearchItems = null;
        GridItemsSource = items;

        OnPropertyChanged(nameof(SchemaSearchItemCollections));
        OnPropertyChanged(nameof(SchemaSearchItems));
        OnPropertyChanged(nameof(GridItemsSource));
    }

    private readonly IMessageForUserTools _messageForUserTools;
    private readonly LogToolViewModel _logToolViewModel;
    private ObservableCollection<SchemaSearchItem> _allItems;
    public SchemaSearchViewModel(IFactory factory, IGeneralApplicationData generalApplicationData, IMessageForUserTools messageForUserTools,
        LogToolViewModel logToolViewModel)
    {
        _generalApplicationData = generalApplicationData;
        _messageForUserTools = messageForUserTools;
        _logToolViewModel = logToolViewModel;
        this.Factory = factory;

        SchemaSearchItemCollections = new ObservableCollection<SchemaSearchItem>();
        _allItems = new ObservableCollection<SchemaSearchItem>();
        GridItemsSource = SchemaSearchItemCollections;
        RefreshDbCmd = new AsyncRelayCommand(RefreshDb);
        GridEnabled = true;

        ConnectionName = _generalApplicationData.Config.ConnectionNameInSchemaSearch;
        CaseSensitive = _generalApplicationData.Config.CaseSensitive;
        SearchInSource = _generalApplicationData.Config.SearchInSource;
        WholeWord = _generalApplicationData.Config.WholeWords;
        RegexMode = _generalApplicationData.Config.RegexMode;
        RefreshStartup = _generalApplicationData.Config.RefreshOnStartupInSchemaSearch;
    }

    public async Task DoubleTappedAction(SchemaSearchItem searchItem)
    {
        string[] toExpandPath = searchItem.GetPath(ConnectionName);

        if (toExpandPath.Length == 0)
        {
            return;
        }

        // A reveal is a user navigation request, not a queue of independent jobs.
        // Cancel the previous request so rapid double-clicks cannot later restore
        // stale selection/focus after the newest request has completed.
        _revealCancellationTokenSource?.Cancel();
        var revealCancellation = new CancellationTokenSource();
        _revealCancellationTokenSource = revealCancellation;

        try
        {
            DbSchemaViewModel? dbChemaViewModel = Factory.Find(a => a is DbSchemaViewModel).FirstOrDefault() as DbSchemaViewModel;
            if (dbChemaViewModel is not null)
            {
                await dbChemaViewModel.ExpandToNodeFull(toExpandPath, revealCancellation.Token);
            }
        }
        catch (OperationCanceledException) when (revealCancellation.IsCancellationRequested)
        {
            // Expected when a newer Schema Search double-click supersedes this one.
        }
        finally
        {
            if (ReferenceEquals(_revealCancellationTokenSource, revealCancellation))
            {
                _revealCancellationTokenSource = null;
            }

            revealCancellation.Dispose();
        }
    }


    public DataGridCollectionView? SchemaSearchItems { get; set; }
    public object GridItemsSource { get; private set; }
    public ObservableCollection<SchemaSearchItem> SchemaSearchItemCollections { get; set; }
    public AsyncRelayCommand RefreshDbCmd { get; set; }

    [ObservableProperty]
    public partial SchemaSearchItem? SelectedSearchItem { get; set; }

    [RelayCommand]
    private void CloseSearchDetails()
    {
        SelectedSearchItem = null;
    }

    private async Task RefreshDb()
    {
        if (ConnectionName is null)
        {
            ConnectionName = "ENTER NAME";
            return;
        }
        RefreshEnabled = false;
        GridEnabled = false;

        SchemaSearchItemCollections.Clear();
        _allItems.Clear();
        
        try
        {
            if (_generalApplicationData.LoginDataDic.ContainsKey(ConnectionName))
            {
                _service = await Task.Run(() => DatabaseServiceHelpers.GetDatabaseService(_generalApplicationData, ConnectionName));
                if (_service is not null)
                {
                    var newAllItems = await Task.Run(async () =>
                    {
                        var localTempItems = new List<SchemaSearchItem>();
                        var databases = _service.GetDatabases("");

                        foreach (var database in databases)
                        {
                            var schemas = _service.GetSchemas(database, "");
                            foreach (var schema in schemas)
                            {
                                var obejctType = new TypeInDatabaseEnum[]
                                {
                                    TypeInDatabaseEnum.Table,
                                    TypeInDatabaseEnum.View,
                                    TypeInDatabaseEnum.Procedure,
                                    TypeInDatabaseEnum.ExternalTable,
                                    TypeInDatabaseEnum.Synonym,
                                    TypeInDatabaseEnum.Function,
                                    TypeInDatabaseEnum.Fluid,
                                    TypeInDatabaseEnum.Index,
                                    TypeInDatabaseEnum.Partition,
                                    TypeInDatabaseEnum.Trigger
                                };

                                for (int i = 0; i < obejctType.Length; i++)
                                {
                                    var tpe = obejctType[i];
                                    var objects = _service.GetDbObjects(database, schema, "", tpe);
                                    foreach (DatabaseObject item in objects)
                                    {
                                        if (tpe == TypeInDatabaseEnum.Procedure)
                                        {
                                            var ll = await _service.GetProceduresSignaturesFromName(database, schema, item.Name);
                                            foreach (var item2 in ll)
                                            {
                                                localTempItems.Add(new SchemaSearchItem()
                                                {
                                                    Id = item.Id,
                                                    Type = tpe.ToStringEx(),
                                                    Name = string.IsNullOrEmpty(item2.ProcedureSignature) ? item.Name : item2.ProcedureSignature,
                                                    Db = database,
                                                    Desc = item.Desc,
                                                    Schema = schema,
                                                    Owner = item.Owner,
                                                    CreationDateTime = item.CreateDateTime
                                                });
                                            }
                                        }
                                        else
                                        {
                                            localTempItems.Add(new SchemaSearchItem()
                                            {
                                                Id = item.Id,
                                                Type = tpe.ToStringEx(),
                                                Name = item.Name,
                                                Db = database,
                                                Desc = item.Desc,
                                                Schema = schema,
                                                Owner = item.Owner,
                                                CreationDateTime = item.CreateDateTime
                                            });
                                        }
                                    }
                                }

                                var columnItems = _service.GetColumnsFromAllTablesAndSchemas(database, schema);
                                foreach (var (column, databaseObject) in columnItems)
                                {
                                    localTempItems.Add(new SchemaSearchItem()
                                    {
                                        Id = -1,
                                        Type = "Column",
                                        Name = column.Name,
                                        Db = database,
                                        Desc = column.Desc,
                                        Schema = schema,
                                        Owner = databaseObject.Owner,
                                        ParentType = databaseObject.TypeInDatabase.ToStringEx(),
                                        ParentName = databaseObject.Name,
                                        MoreInfo = $"column from {databaseObject.Name}({databaseObject.TypeInDatabase.ToStringEx()})",
                                        CreationDateTime = databaseObject.CreateDateTime
                                    });
                                }
                            }
                        }

                        SortSchemaItemsByTypeAndName(localTempItems);

                        return new ObservableCollection<SchemaSearchItem>(localTempItems);
                    });

                    _allItems = newAllItems;
                    await ApplyFilter();

                    await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

                    if (SearchInSource)
                    {
                        await _service.CacheAllObjects(new TypeInDatabaseEnum[] { TypeInDatabaseEnum.Procedure,
                            TypeInDatabaseEnum.View, TypeInDatabaseEnum.ExternalTable, TypeInDatabaseEnum.Synonym
                    });
                    }
                }
                else
                {
                    _messageForUserTools.ShowSimpleMessageBoxInstance("cannot connect to database", "Warning");
                }
            }
        }
        catch (Exception e)
        {
            _generalApplicationData.GlobalLoggerObject.TrackError(e, isCrash: false);
            _logToolViewModel.AddLog(e.Message, LogMessageType.error, "Error", DateTime.Now, "schema search");
        }
        finally
        {
            RefreshEnabled = true;
            GridEnabled = true;
        }
    }


    public void TryGoupResults(int groupLimit = GROUP_LIMIT)
    {
        // Grouping disabled — scroll uses uniform-height ProDataGrid fast path.
    }

    public string SearchText
    {
        get;
        set
        {
            SetProperty(ref field, value);
            if (searchTimer is null)
            {
                searchTimer = new Avalonia.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(300)
                };
                searchTimer.Tick += Timer_Tick;
            }
            searchTimer.Stop();
            searchTimer.Start();
        }
    }

    private Avalonia.Threading.DispatcherTimer searchTimer;
    private const int GROUP_LIMIT = 20;
    public const int GIANT_GROUP_LIMIT = 100;

    public ISchemaSearchViewBridge? ViewBridge { get; set; }
    private void Timer_Tick(object? sender, EventArgs e)
    {
        searchTimer.Stop();
        //GridEnabled = false;
        RefreshRegex();
        AfterOptionsChange();
        //GridEnabled = true;
    }

    [ObservableProperty]
    public partial bool RefreshEnabled { get; set; } = true;

    partial void AfterOptionsChange()
    {
        // Apply filter asynchronously to avoid UI blocking
        _ = ApplyFilter();
        TryGoupResults();
    }

    public void ForceRefreshFilter()
    {
        _ = ApplyFilter();
    }

    private CancellationTokenSource? _filterCancellationTokenSource;
    private CancellationTokenSource? _revealCancellationTokenSource;
    
    private async Task ApplyFilter()
    {
        // Cancel previous filter operation
        _filterCancellationTokenSource?.Cancel();
        _filterCancellationTokenSource = new CancellationTokenSource();
        var token = _filterCancellationTokenSource.Token;
        
        GridEnabled = false;

        try
        {
            // Run filtering in background thread
            var filteredItems = await Task.Run(() =>
            {
                var result = new List<SchemaSearchItem>();
                foreach (var item in _allItems)
                {
                    if (token.IsCancellationRequested)
                        return null;
                    
                    if (IsFilterOk(item))
                    {
                        result.Add(item);
                    }
                }
                return result;
            }, token);
            
            // Check if operation was cancelled
            if (filteredItems == null || token.IsCancellationRequested)
                return;

            SortSchemaItemsByTypeAndName(filteredItems);
            var newCollection = ToDisplayCollection(filteredItems);
            PublishGridItems(newCollection);
            
            // Small delay to allow UI to render
            await Task.Delay(30);
            
            TryGoupResults();
        }
        catch (OperationCanceledException)
        {
            // Operation was cancelled, ignore
        }
        finally
        {
            GridEnabled = true;
        }
    }

    public void Dispose()
    {
        _filterCancellationTokenSource?.Cancel();
        _filterCancellationTokenSource?.Dispose();
        _filterCancellationTokenSource = null;

        _revealCancellationTokenSource?.Cancel();
        _revealCancellationTokenSource?.Dispose();
        _revealCancellationTokenSource = null;

        if (searchTimer is not null)
        {
            searchTimer.Tick -= Timer_Tick;
            searchTimer.Stop();
        }

        GC.SuppressFinalize(this);
    }

    [ObservableProperty]
    public partial bool ShowSettings { get; set; }

    private bool IsFilterOk(SchemaSearchItem item)
    {
        if (SearchText is null && item.Type != "Column")
        {
            return true;
        }
        if (SearchText is null || SearchText.Length <= 2 && item.Type == "Column")
        {
            return false;
        }

        if (!string.IsNullOrEmpty(SearchText))
        {
            if (WholeWord || RegexMode)
            {
                if (RxWholeWorld is not null)
                {
                    return
                        ColumnFilters(item) && (
                        item.Name is not null && RxWholeWorld.IsMatch(item.Name) ||
                        item.Desc is not null && RxWholeWorld.IsMatch(item.Desc) ||
                        SearchInSource && (item.Type == "Procedure" || item.Type == "View" || item.Type == "External table" || item.Type == "Synonym")
                        && _service.IsItemSourceContains(DatabaseServiceHelpers.FromStringEx(item.Type), item.Db, item.Schema, item.Name, item.Id, _currentStringComparation, null, RxWholeWorld));
                }
            }
            else
            {
                return
                    ColumnFilters(item) && (
                    item.Name is not null && item.Name.Contains(SearchText, _currentStringComparation) ||
                    item.Desc is not null && item.Desc.Contains(SearchText, _currentStringComparation) ||
                    SearchInSource && (item.Type == "Procedure" || item.Type == "View" || item.Type == "External table" || item.Type == "Synonym")
                    && _service.IsItemSourceContains(DatabaseServiceHelpers.FromStringEx(item.Type), item.Db, item.Schema, item.Name, item.Id, _currentStringComparation, SearchText, null));
            }
        }
        return true;
    }
    private bool ColumnFilters(SchemaSearchItem item)
    {
        return (string.IsNullOrEmpty(TypeFilterString) || item.Type?.Contains(TypeFilterString, _currentStringComparation) == true)
            && (string.IsNullOrEmpty(NameFilterString) || item.Name?.Contains(NameFilterString, _currentStringComparation) == true)
            && (string.IsNullOrEmpty(DbFilterString) || item.Db?.Contains(DbFilterString, _currentStringComparation) == true)
            && (string.IsNullOrEmpty(DescFilterString) || item.Desc?.Contains(DescFilterString, _currentStringComparation) == true)
            && (string.IsNullOrEmpty(SchemaFilterString) || item.Schema?.Contains(SchemaFilterString, _currentStringComparation) == true)
            && (string.IsNullOrEmpty(OwnerFilterString) || item.Owner?.Contains(OwnerFilterString, _currentStringComparation) == true)
            ;
    }
}
