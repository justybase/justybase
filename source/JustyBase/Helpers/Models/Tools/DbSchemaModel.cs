using Avalonia.Threading;
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

    public DbSchemaModel(TypeInDatabaseEnum typeInDatabase, DatabaseTypeEnum databaseTypeEnum, IGeneralApplicationData generalApplicationData)
    {
        _generalApplicationData = generalApplicationData;
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

    private bool _childrenLoaded = false;
    private Task? _loadChildrenTask;

    public void ClearChildren()
    {
        _children.Clear();
        _childrenLoaded = false;
        _loadChildrenTask = null;
    }

    private readonly ObservableCollection<DbSchemaModel> _children = [];

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
            if (ActualTypeInDatabase == TypeInDatabaseEnum.Connection
                && DatabaseServiceHelpers.GetDatabaseConnectedLevel(Name) < DatabaseConnectedLevel.ConnectedDatabaseObjects)
            {
                _children.Add(new DbSchemaModel(TypeInDatabaseEnum.otherNoneEntry, this.DatabaseTypeEnumValue, _generalApplicationData)
                {
                    Name = "Loading...",
                    Parent = this,
                    ConnectionName = this.ConnectionName,
                    IsExpandedable = false
                });

                try
                {
                    await Task.Run(() => DatabaseServiceHelpers.GetDatabaseService(_generalApplicationData, Name))
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
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _children.Clear();
                _loadChildrenTask = null;
            });
        }
    }
}
