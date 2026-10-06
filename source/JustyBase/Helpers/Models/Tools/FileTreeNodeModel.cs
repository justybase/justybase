using CommunityToolkit.Mvvm.ComponentModel;
using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using System.Collections.ObjectModel;
using System.Globalization;

namespace JustyBase.Models.Tools;

public partial class FileTreeNodeModel : ObservableObject, IDisposable
{
    /// <summary>
    /// When &gt; 0, background directory enumeration sleeps this many ms.
    /// Used by headless responsiveness tests to simulate slow FS without blocking the UI thread.
    /// </summary>
    public static int SimulateEnumerateDelayMs { get; set; }

    private FileSystemWatcher? _watcher;
    private readonly ObservableCollection<FileTreeNodeModel> _children = [];
    private bool _childrenLoaded;
    private bool _loadingStarted;

    private readonly ISimpleLogger _simpleLogger;

    private readonly IMessageForUserTools _messageForUserTools;
    public FileTreeNodeModel(
        string path,
        bool isDirectory,
        bool isRoot,
        IMessageForUserTools messageForUserTools,
        ISimpleLogger simpleLogger)
    {
        Path = path;
        Name = isRoot ? ShortRootName(path) : System.IO.Path.GetFileName(Path);
        IsExpanded = false;
        IsDirectory = isDirectory;
        _messageForUserTools = messageForUserTools;
        _simpleLogger = simpleLogger;

        if (!isDirectory)
        {
            var info = new FileInfo(path);
            Size = info.Length;
            Modified = info.LastWriteTimeUtc;
        }
    }

    [ObservableProperty]
    public partial string Path { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial long? Size { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? Modified { get; set; }

    [ObservableProperty]
    public partial bool HasChildren { get; set; } = true;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public string FormattedSize
    {
        get
        {
            if (Size is null)
            {
                return string.Empty;
            }
            else
            {
                if (Size <= 1024 * 1024)
                {
                    double l = (double)(Size / 1024.0);
                    return l.ToString("N0", CultureInfo.CurrentCulture) + " KB";
                }
                else
                {
                    double l = (double)(Size / 1024.0 / 1024.0);
                    return l.ToString("N1", CultureInfo.CurrentCulture) + " MB";
                }
            }
        }
    }


    public bool IsDirectory { get; }

    /// <summary>
    /// True once children were enumerated. Reading <see cref="Children"/> on an
    /// unloaded directory starts a background load, so collapse-all style walks
    /// should check this flag first instead of touching <see cref="Children"/>.
    /// </summary>
    public bool AreChildrenLoaded => _childrenLoaded;

    public IReadOnlyList<FileTreeNodeModel> Children
    {
        get
        {
            if (IsDirectory && !_childrenLoaded && !_loadingStarted)
            {
                _ = LoadChildrenAsync();
            }

            return _children;
        }
    }

    public async Task LoadChildrenAsync()
    {
        if (!IsDirectory)
        {
            throw new NotSupportedException();
        }

        if (_childrenLoaded || _loadingStarted)
        {
            return;
        }

        _loadingStarted = true;

        try
        {
            var path = Path;
            var delayMs = SimulateEnumerateDelayMs;
            var enumerated = await Task.Run(() =>
            {
                if (delayMs > 0)
                {
                    Thread.Sleep(delayMs);
                }

                var options = new EnumerationOptions { IgnoreInaccessible = true };
                var directories = Directory.EnumerateDirectories(path, "*", options).ToList();
                var files = Directory.EnumerateFiles(path, "*", options).ToList();
                return (directories, files);
            }).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _children.Clear();

                foreach (var d in enumerated.directories.OrderBy(static d => System.IO.Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
                {
                    _children.Add(new FileTreeNodeModel(d, true, false, _messageForUserTools, _simpleLogger));
                }

                foreach (var f in enumerated.files.OrderBy(static f => System.IO.Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                {
                    _children.Add(new FileTreeNodeModel(f, false, false, _messageForUserTools, _simpleLogger)
                    {
                        HasChildren = false
                    });
                }

                AttachWatcher(path);

                if (_children.Count == 0)
                {
                    HasChildren = false;
                }

                _childrenLoaded = true;
            });
        }
        catch (Exception ex)
        {
            _simpleLogger.TrackError(ex, isCrash: false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _loadingStarted = false;
            });
        }
    }

    private void AttachWatcher(string path)
    {
        _watcher?.Dispose();
        _watcher = new FileSystemWatcher
        {
            Path = path,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
        };

        _watcher.Changed += OnChanged;
        _watcher.Created += OnCreated;
        _watcher.Deleted += OnDeleted;
        _watcher.Renamed += OnRenamed;
        try
        {
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            _simpleLogger.TrackError(ex, isCrash: false);
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;

        foreach (var child in _children)
        {
            child.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Re-reads this directory in place, preserving the node (and its expansion).
    /// Loaded children are disposed and enumerated again; collapsed nodes just
    /// drop their cache so the next expand loads fresh.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (!IsDirectory)
        {
            return;
        }

        _watcher?.Dispose();
        _watcher = null;
        foreach (var child in _children)
        {
            child.Dispose();
        }

        _children.Clear();
        _childrenLoaded = false;
        _loadingStarted = false;
        HasChildren = true;

        if (IsExpanded)
        {
            await LoadChildrenAsync().ConfigureAwait(false);
        }
    }

    private static string ShortRootName(string path)
    {
        var trimmed = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var name = System.IO.Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    public static Comparison<FileTreeNodeModel?> SortAscending<T>(Func<FileTreeNodeModel, T> selector)
    {
        return (x, y) =>
        {
            if (x is null && y is null)
                return 0;
            else if (x is null)
                return -1;
            else if (y is null)
                return 1;
            if (x.IsDirectory == y.IsDirectory)
                return Comparer<T>.Default.Compare(selector(x), selector(y));
            else if (x.IsDirectory)
                return -1;
            else
                return 1;
        };
    }

    public static Comparison<FileTreeNodeModel?> SortDescending<T>(Func<FileTreeNodeModel, T> selector)
    {
        return (x, y) =>
        {
            if (x is null && y is null)
                return 0;
            else if (x is null)
                return 1;
            else if (y is null)
                return -1;
            if (x.IsDirectory == y.IsDirectory)
                return Comparer<T>.Default.Compare(selector(y), selector(x));
            else if (x.IsDirectory)
                return -1;
            else
                return 1;
        };
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (e.ChangeType == WatcherChangeTypes.Changed && File.Exists(e.FullPath))
        {
            _messageForUserTools.DispatcherActionInstance(() =>
            {
                foreach (var child in _children)
                {
                    if (child.Path == e.FullPath)
                    {
                        if (!child.IsDirectory)
                        {
                            try
                            {
                                var info = new FileInfo(e.FullPath);
                                child.Size = info.Length;
                                child.Modified = info.LastWriteTimeUtc;
                            }
                            catch (Exception ex)
                            {
                                _simpleLogger.TrackError(ex, isCrash: false);
                            }
                        }
                        break;
                    }
                }
            });
        }
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        if (File.Exists(e.FullPath) || Directory.Exists(e.FullPath))
        {
            _messageForUserTools.DispatcherActionInstance(() =>
            {
                var node = new FileTreeNodeModel(e.FullPath, File.GetAttributes(e.FullPath).HasFlag(FileAttributes.Directory), false, _messageForUserTools, _simpleLogger);
                _children.Add(node);
            });
        }
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        _messageForUserTools.DispatcherActionInstance(() =>
        {
            for (var i = 0; i < _children.Count; ++i)
            {
                if (_children[i].Path == e.FullPath)
                {
                    _children.RemoveAt(i);
                    System.Diagnostics.Debug.WriteLine($"Removed {e.FullPath}");
                    break;
                }
            }
        });
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        _messageForUserTools.DispatcherActionInstance(() =>
        {
            foreach (var child in _children)
            {
                if (child.Path == e.OldFullPath)
                {
                    child.Path = e.FullPath;
                    child.Name = e.Name ?? string.Empty;
                    break;
                }
            }
        });
    }
}
