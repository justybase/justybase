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

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? LoadError { get; set; }

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
    private CancellationTokenSource? _loadCts;
    private int _loadGeneration;
    private const int MaxChildrenBeforeTruncation = 500;
    private const int MaxErrorMessageLength = 200;

    public void ClearChildren()
    {
        // Bump the generation first so an in-flight load cannot resurrect its
        // results (or null out a newer task) after this reset.
        _loadGeneration++;

        var cts = _loadCts;
        _loadCts = null;
        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already finished; the owning load disposes it.
            }
        }

        _children.Clear();
        _childrenLoaded = false;
        _loadChildrenTask = null;
        LoadError = null;
        IsLoading = false;
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

    public Task LoadChildrenAsync(CancellationToken cancellationToken = default)
    {
        if (_childrenLoaded)
            return Task.CompletedTask;

        if (_loadChildrenTask is not null)
            return _loadChildrenTask;

        var previousCts = _loadCts;
        if (previousCts is not null)
        {
            try
            {
                previousCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The owning load already released it.
            }
        }

        var generation = ++_loadGeneration;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCts = cts;
        _loadChildrenTask = LoadChildrenCoreAsync(cts, generation);
        return _loadChildrenTask;
    }

    private async Task LoadChildrenCoreAsync(CancellationTokenSource cts, int generation)
    {
        var cancellationToken = cts.Token;
        IsLoading = true;
        LoadError = null;
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

            cancellationToken.ThrowIfCancellationRequested();

            var loadedChildren = await Task.Run(() =>
            {
                var collection = new ObservableCollection<DbSchemaModel>();
                LoadChildren(collection);
                return collection;
            }, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation != _loadGeneration)
                {
                    // A reset or newer load superseded this one; drop the results.
                    return;
                }

                _children.Clear();
                if (loadedChildren.Count > MaxChildrenBeforeTruncation)
                {
                    for (var i = 0; i < MaxChildrenBeforeTruncation; i++)
                    {
                        _children.Add(loadedChildren[i]);
                    }

                    _children.Add(new DbSchemaModel(TypeInDatabaseEnum.otherNoneEntry, DatabaseTypeEnumValue, _generalApplicationData, _resolver)
                    {
                        Name = $"… +{loadedChildren.Count - MaxChildrenBeforeTruncation} more (use search)",
                        Parent = this,
                        ConnectionName = ConnectionName,
                        IsExpandedable = false,
                    });
                }
                else
                {
                    foreach (var child in loadedChildren)
                    {
                        _children.Add(child);
                    }
                }

                _childrenLoaded = true;
                IsLoading = false;
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Keep previous children on cancel; a newer load superseded this one.
                if (generation == _loadGeneration)
                {
                    IsLoading = false;
                }
            });
        }
        catch (Exception ex)
        {
            // Never swallow schema-load failures silently: in trimmed/AOT
            // builds the first symptom of an incompatible API is an empty
            // tree, and without this log entry there is nothing to diagnose.
            _generalApplicationData.GlobalLoggerObject.TrackError(ex, isCrash: false);
            if (generation == _loadGeneration)
            {
                _loadChildrenTask = null;
            }

            var rawMessage = ex.Message;
            var message = SummarizeError(rawMessage);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation != _loadGeneration)
                {
                    return;
                }

                _children.Clear();
                _children.Add(new DbSchemaModel(TypeInDatabaseEnum.otherNoneEntry, DatabaseTypeEnumValue, _generalApplicationData, _resolver)
                {
                    Name = $"⚠ {message}",
                    Parent = this,
                    ConnectionName = ConnectionName,
                    IsExpandedable = false,
                });
                LoadError = rawMessage;
                IsLoading = false;
            });
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                _loadCts = null;
            }

            cts.Dispose();
        }
    }

    /// <summary>
    /// Provider messages can be multi-line and echo server details; keep the tree
    /// node readable and let the full text live in the log / <see cref="LoadError"/>.
    /// </summary>
    private static string SummarizeError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Failed to load schema";
        }

        var firstLine = message
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?
            .Trim();

        if (string.IsNullOrEmpty(firstLine))
        {
            return "Failed to load schema";
        }

        return firstLine.Length <= MaxErrorMessageLength
            ? firstLine
            : firstLine[..MaxErrorMessageLength] + "…";
    }
}
