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
using JustyBase.Services.Schema;
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
    private readonly Services.Documents.IDatabaseServiceResolver? _resolver;
    private readonly SourceIndexService? _sourceIndexService;
    private ObservableCollection<SchemaSearchItem> _allItems;
    public SchemaSearchViewModel(IFactory factory, IGeneralApplicationData generalApplicationData, IMessageForUserTools messageForUserTools,
        LogToolViewModel logToolViewModel, Services.Documents.IDatabaseServiceResolver? resolver = null,
        SourceIndexService? sourceIndexService = null)
    {
        _generalApplicationData = generalApplicationData;
        _messageForUserTools = messageForUserTools;
        _logToolViewModel = logToolViewModel;
        _resolver = resolver;
        _sourceIndexService = sourceIndexService;
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

        try
        {
            if (_generalApplicationData.LoginDataDic.ContainsKey(ConnectionName))
            {
                // F1: prefer DI resolver; fall back to static helpers for legacy paths.
                _service = _resolver is not null
                    ? await Task.Run(() => _resolver.GetDatabaseService(_generalApplicationData, ConnectionName))
                    : await Task.Run(() => DatabaseServiceHelpers.GetDatabaseService(_generalApplicationData, ConnectionName));
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
                        // The definition index loads its own source snapshot; a schema
                        // refresh must invalidate it instead of re-reading the old cache.
                        _sourceIndexService?.Invalidate(ConnectionName);

                        // Re-run the filter now that the source index can see the fresh
                        // snapshot; otherwise source-only matches appear only after a later edit.
                        await ApplyFilter();
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
            Dictionary<SourceMatchKey, SourceTextHit>? sourceMatches = null;
            if (SearchInSource
                && _sourceIndexService is not null
                && _service is not null
                && !string.IsNullOrWhiteSpace(ConnectionName)
                && !string.IsNullOrWhiteSpace(SearchText))
            {
                try
                {
                    var result = await _sourceIndexService.SearchAsync(
                        ConnectionName,
                        new SourceTextQuery
                        {
                            Term = SearchText,
                            CaseSensitive = CaseSensitive,
                            WholeWord = WholeWord,
                            Regex = RegexMode,
                            IgnoreComments = false,
                            ObjectTypes = ["PROCEDURE", "VIEW", "EXTERNAL TABLE"],
                            Limit = 2000,
                        },
                        refresh: false,
                        cancellationToken: token);
                    sourceMatches = new Dictionary<SourceMatchKey, SourceTextHit>();
                    foreach (var hit in result.Hits)
                    {
                        sourceMatches[SourceMatchKey.Create(hit.Object)] = hit;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ArgumentException ex)
                {
                    // Invalid regular expression: the name/description filters already
                    // use a never-match regex in this case.
                    _generalApplicationData.GlobalLoggerObject.TrackError(ex, isCrash: false);
                    sourceMatches = [];
                }
                catch (InvalidOperationException ex)
                {
                    _generalApplicationData.GlobalLoggerObject.TrackError(ex, isCrash: false);
                    sourceMatches = [];
                }
            }

            var matches = sourceMatches;
            // Run filtering in background thread
            var filteredItems = await Task.Run(() =>
            {
                var result = new List<SchemaSearchItem>();
                foreach (var item in _allItems)
                {
                    if (token.IsCancellationRequested)
                        return null;
                    
                    if (IsFilterOk(item, matches))
                    {
                        if (matches is not null && matches.TryGetValue(SourceMatchKey.Create(item), out var hit))
                        {
                            result.Add(item with
                            {
                                SourceLine = hit.Line,
                                SourceSnippet = hit.Snippet,
                                SourceMatches = hit.Matches,
                            });
                        }
                        else
                        {
                            result.Add(item);
                        }
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

    /// <summary>Stable, case-insensitive lookup key shared by schema rows and source hits.</summary>
    private readonly record struct SourceMatchKey(string Database, string Schema, string Name, string Type)
    {
        public static SourceMatchKey Create(SchemaSearchItem item)
            => new(
                item.Db?.ToUpperInvariant() ?? "",
                item.Schema?.ToUpperInvariant() ?? "",
                SourceNameOf(item).ToUpperInvariant(),
                NormalizeType(item.Type));

        public static SourceMatchKey Create(SourceObjectRef source)
            => new(
                source.Database.ToUpperInvariant(),
                source.Schema.ToUpperInvariant(),
                source.Name.ToUpperInvariant(),
                NormalizeType(source.ObjectType));

        private static string NormalizeType(string type)
            => type.Replace(" ", "", StringComparison.Ordinal)
                .Replace("_", "", StringComparison.Ordinal)
                .ToUpperInvariant();
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

    private bool IsFilterOk(SchemaSearchItem item, IReadOnlyDictionary<SourceMatchKey, SourceTextHit>? sourceMatches)
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
                        SourceSearchMatches(item, sourceMatches));
                }
            }
            else
            {
                return
                    ColumnFilters(item) && (
                    item.Name is not null && item.Name.Contains(SearchText, _currentStringComparation) ||
                    item.Desc is not null && item.Desc.Contains(SearchText, _currentStringComparation) ||
                    SourceSearchMatches(item, sourceMatches));
            }
        }
        return true;
    }

    private bool SourceSearchMatches(
        SchemaSearchItem item,
        IReadOnlyDictionary<SourceMatchKey, SourceTextHit>? sourceMatches)
    {
        if (!SearchInSource)
        {
            return false;
        }

        if (sourceMatches is not null && item.Type != "Synonym")
        {
            return sourceMatches.ContainsKey(SourceMatchKey.Create(item));
        }

        return SearchInSource
            && item.Type is "Procedure" or "View" or "External table" or "Synonym"
            && _service is not null
            && _service.IsItemSourceContains(
                DatabaseServiceHelpers.FromStringEx(item.Type),
                item.Db,
                item.Schema,
                SourceNameOf(item),
                item.Id,
                _currentStringComparation,
                SearchText,
                null);
    }

    private static string SourceNameOf(SchemaSearchItem item)
    {
        if (item.Type != "Procedure")
        {
            return item.Name;
        }

        int parenthesis = item.Name.IndexOf('(');
        return parenthesis > 0 ? item.Name[..parenthesis].Trim() : item.Name;
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
