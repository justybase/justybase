using CommunityToolkit.Mvvm.ComponentModel;
using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginDatabaseBase.Database;
using System.Collections.ObjectModel;

namespace JustyBase.Models.Tools;

public sealed partial class DbSchemaModel
{
    [ObservableProperty]
    public partial DbSchemaModel Self { get; set; }

    [ObservableProperty]
    public partial string Info { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsExpandedable { get; set; }

    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly Services.Documents.IDatabaseServiceResolver? _resolver;

    /// <summary>
    /// F1: Global fallback so manually-constructed models (new DbSchemaModel(...))
    /// can resolve via DI without threading the resolver through every call site.
    /// Set once from the composition root; per-instance ctor param takes precedence.
    /// </summary>
    public static Func<Services.Documents.IDatabaseServiceResolver?>? ResolverProvider { get; set; }

    public DbSchemaModel(TypeInDatabaseEnum typeInDatabase, DatabaseTypeEnum databaseTypeEnum, IGeneralApplicationData generalApplicationData, Services.Documents.IDatabaseServiceResolver? resolver = null)
    {
        _generalApplicationData = generalApplicationData;
        _resolver = resolver ?? ResolverProvider?.Invoke();
        DatabaseTypeEnumValue = databaseTypeEnum;
        ActualTypeInDatabase = typeInDatabase;
        IsExpandedable = GetExpInfo();
        Self = this;
    }

    private bool GetExpInfo()
    {
        return ActualTypeInDatabase switch
        {
            TypeInDatabaseEnum.ColumnDataType => false,
            TypeInDatabaseEnum.ColumnDataTypeNullInfo => false,
            TypeInDatabaseEnum.ColumnComment => false,
            TypeInDatabaseEnum.otherNoneEntry => false,
            _ => true
        };
    }

    private bool _childrenLoaded;
    private Task? _loadChildrenTask;

    public void ClearChildren()
    {
        _children.Clear();
        _childrenLoaded = false;
        _loadChildrenTask = null;
    }

    private readonly ObservableCollection<DbSchemaModel> _children = [];

    /// <summary>
    /// F1: Prefer DI resolver; fall back to static helpers for legacy paths.
    /// </summary>
    internal JustyBase.PluginCommon.Contracts.IDatabaseService? ResolveService(string? connectionName, bool forceRefresh = false)
    {
        if (string.IsNullOrWhiteSpace(connectionName))
        {
            return null;
        }

        if (_resolver is not null)
        {
            return _resolver.GetDatabaseService(_generalApplicationData, connectionName, forceRefresh: forceRefresh);
        }

        return DatabaseServiceHelpers.GetDatabaseService(_generalApplicationData, connectionName, forceRefresh: forceRefresh);
    }

    internal Services.Documents.IDatabaseServiceResolver? Resolver => _resolver;

    public ObservableCollection<DbSchemaModel> Children
    {
        get
        {
            if (!_childrenLoaded && _loadChildrenTask is null)
            {
                _ = LoadChildrenAsync();
            }
            return _children;
        }
    }

    public Task LoadChildrenAsync()
    {
        if (_childrenLoaded)
            return Task.CompletedTask;

        if (_loadChildrenTask is not null)
            return _loadChildrenTask;

        _loadChildrenTask = LoadChildrenCoreAsync();
        return _loadChildrenTask;
    }

    private async Task LoadChildrenCoreAsync()
    {
        try
        {
            // F1: connected-level check prefers resolver when available.
            var connectedLevel = _resolver is not null && !string.IsNullOrWhiteSpace(Name)
                ? _resolver.GetCachedServices().FirstOrDefault(s => s.Name == Name)?.ConnectedLevel
                    ?? DatabaseConnectedLevel.NotConnected
                : DatabaseServiceHelpers.GetDatabaseConnectedLevel(Name);
            if (ActualTypeInDatabase == TypeInDatabaseEnum.Connection
                && connectedLevel < DatabaseConnectedLevel.ConnectedDatabaseObjects)
            {
                _children.Add(new DbSchemaModel(TypeInDatabaseEnum.otherNoneEntry, this.DatabaseTypeEnumValue, _generalApplicationData, _resolver)
                {
                    Name = "Loading...",
                    Parent = this,
                    ConnectionName = this.ConnectionName,
                    IsExpandedable = false
                });

                try
                {
                    await Task.Run(() => ResolveService(Name))
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Database connection preload failed - will retry on demand when user expands nodes
                }
            }

            var loadedChildren = await Task.Run(() =>
            {
                var collection = new ObservableCollection<DbSchemaModel>();
                LoadChildren(collection);
                return collection;
            }).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _children.Clear();
                foreach (var child in loadedChildren)
                {
                    _children.Add(child);
                }

                _childrenLoaded = true;
            });
        }
        catch (Exception ex)
        {
            // Never swallow schema-load failures silently: in trimmed/AOT
            // builds the first symptom of an incompatible API is an empty
            // tree, and without this log entry there is nothing to diagnose.
            _generalApplicationData.GlobalLoggerObject.TrackError(ex, isCrash: false);
            _loadChildrenTask = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _children.Clear();
            });
        }
    }
}
