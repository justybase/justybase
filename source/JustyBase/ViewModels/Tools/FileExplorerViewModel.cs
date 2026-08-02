using Avalonia.Collections;
using Avalonia.Controls.DataGridHierarchical;
using Avalonia.Data;
using Avalonia.Data.Core;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Converters;
using JustyBase.Models.Tools;
using JustyBase.Services;
using JustyBase.Helpers;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace JustyBase.ViewModels.Tools;

public partial class FileExplorerViewModel : Tool
{
    private readonly ISearchInFiles _searchInFiles;
    private readonly IAvaloniaSpecificHelpers _avaloniaSpecificHelpers;
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly LogToolViewModel _logToolViewModel;
    public FileExplorerViewModel(IFactory factory, ISearchInFiles searchInFiles, IAvaloniaSpecificHelpers avaloniaSpecificHelpers, IGeneralApplicationData generalApplicationData, IMessageForUserTools messageForUserTools,
        LogToolViewModel logToolViewModel)
    {
        this.Factory = factory;
        _searchInFiles = searchInFiles;
        _avaloniaSpecificHelpers = avaloniaSpecificHelpers;
        _generalApplicationData = generalApplicationData;
        _messageForUserTools = messageForUserTools;
        _logToolViewModel = logToolViewModel;
        RefreshFileListCmd = new AsyncRelayCommand(RefreshFileList);
        SearchInFilesCommand = new AsyncRelayCommand(DoSearchInFiles);
        OpenDirectoryDialogCmd = new AsyncRelayCommand(OpenDirectoryDialog);

        ShowInExplorerCommand = new RelayCommand(OpenInExplorer);
        OpenInExplorerGridCmd = new RelayCommand(OpenInExplorerGrid);
        CopyFullFilePathCmd = new AsyncRelayCommand(CopyFullFilePathAsync);
        RemoveFileOrDirectoryCmd = new AsyncRelayCommand(RemoveFileOrDirectory);

        using (var fileStream = AssetLoader.Open(new Uri("avares://JustyBase/Assets/file.png")))
        using (var folderStream = AssetLoader.Open(new Uri("avares://JustyBase/Assets/folder.png")))
        using (var folderOpenStream = AssetLoader.Open(new Uri("avares://JustyBase/Assets/folder-open.png")))
        {
            // FolderIconConverter owns these bitmaps for the lifetime of the view model.
#pragma warning disable CA2000
            var fileIcon = new Bitmap(fileStream);
            var folderIcon = new Bitmap(folderStream);
            var folderOpenIcon = new Bitmap(folderOpenStream);
#pragma warning restore CA2000

            _folderIconConverter = new FolderIconConverter(fileIcon, folderOpenIcon, folderIcon);
        }

        WholeWords = false;

        var options = new HierarchicalOptions<FileTreeNodeModel>
        {
            ChildrenSelector = item => item.Children,
            IsExpandedSelector = item => item.IsExpanded,
            IsExpandedSetter = (item, value) => item.IsExpanded = value,
            IsLeafSelector = item => !item.HasChildren
        };

        HierarchicalModel = new HierarchicalModel<FileTreeNodeModel>(options);

        var nameColumn = new DataGridHierarchicalColumnDefinition
        {
            Header = "Name",
            Binding = CreateNodeBinding<string>("Name", item => item.Name),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        };

        var sizeColumn = new DataGridTextColumnDefinition
        {
            Header = "Size",
            Binding = CreateNodeBinding<string>("Size", item => item.FormattedSize),
            Width = new DataGridLength(100, DataGridLengthUnitType.Pixel)
        };

        var modifiedColumn = new DataGridTextColumnDefinition
        {
            Header = "Modified",
            Binding = CreateNodeBinding<DateTimeOffset?>("Modified", item => item.Modified),
            Width = new DataGridLength(150, DataGridLengthUnitType.Pixel)
        };

        ColumnDefinitions = new ObservableCollection<DataGridColumnDefinition>
        {
            nameColumn,
            sizeColumn,
            modifiedColumn
        };

        SearchItemCollections = new ObservableCollection<SearchItem>();
        SearchItems = new DataGridCollectionView(SearchItemCollections)
        {
            GroupDescriptions =
            {
                    new DataGridPathGroupDescription(nameof(SearchItem.Type))
            }
        };
        //var sortOrder = DataGridSortDescription.FromPath("Last write time", ListSortDirection.Descending);
        //SearchItems.SortDescriptions.Add(sortOrder);

        if (_generalApplicationData.Config.StartsFolderPaths?.Count > 0 && Directory.Exists(_generalApplicationData.Config.StartsFolderPaths[0]))
        {
            InitialFilePath = string.Join(';', _generalApplicationData.Config.StartsFolderPaths);
        }
        else
        {
            InitialFilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        _startupTimer.Interval = TimeSpan.FromSeconds(1);
        _startupTimer.Tick += Timer_Tick;
        _startupTimer.Start();
    }
    public HierarchicalModel<FileTreeNodeModel> HierarchicalModel { get; }
    public ObservableCollection<DataGridColumnDefinition> ColumnDefinitions { get; }
    public DataGridCollectionView SearchItems { get; set; }
    public ObservableCollection<SearchItem> SearchItemCollections { get; set; }

    private readonly FolderIconConverter? _folderIconConverter;
    //private FileTreeNodeModel? _root;
    //private FileTreeNodeModel? _rootData;

    [ObservableProperty]
    public partial string InitialFilePath { get; set; }
    private string _searchText = "";
    private DispatcherTimer? _searchTimer;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                SearchInFiles = false;
                if (_searchTimer is null)
                {
                _searchTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                    _searchTimer.Tick += SearchTimer_Tick;
                }
                _searchTimer.Stop();
                _searchTimer.Start();
            }
        }
    }

    private void SearchTimer_Tick(object? sender, EventArgs e)
    {
        _searchTimer?.Stop();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        List<SearchItem> filteredList = new(_allSearchItems.Count);
        foreach (var item in _allSearchItems)
        {
            if (SearchInFiles)
            {
                if (item.IsFounded)
                {
                    filteredList.Add(item);
                }
            }
            else
            {
                if (string.IsNullOrEmpty(SearchText) || item.ShortName.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                {
                    filteredList.Add(item);
                }
            }
        }
        SearchItemCollections = new ObservableCollection<SearchItem>(filteredList);
        SearchItems = new DataGridCollectionView(SearchItemCollections);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(SearchItems)));
    }

    public ICommand RefreshFileListCmd { get; set; }
    public ICommand SearchInFilesCommand { get; set; }
    public ICommand OpenDirectoryDialogCmd { get; set; }
    public ICommand ShowInExplorerCommand { get; set; }
    public ICommand OpenInExplorerGridCmd { get; set; }
    public ICommand CopyFullFilePathCmd { get; set; }
    public ICommand RemoveFileOrDirectoryCmd { get; set; }

    private readonly List<SearchItem> _filesList = [];
    private readonly List<SearchItem> _directoryList = [];
    private readonly List<SearchItem> _allSearchItems = [];

    /// <summary>
    /// Full paths of SQL files known to the Files panel (<see cref="SearchItem.Name"/>).
    /// </summary>
    public IReadOnlyList<string> GetKnownSqlFilePaths()
        => _filesList
            .Where(i => !string.IsNullOrWhiteSpace(i.Name)
                && i.Name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Name)
            .ToArray();

    private async Task RefreshFileList()
    {
        _filesList.Clear();
        _directoryList.Clear();
        _allSearchItems.Clear();
        await DoInitialSearch();
        _allSearchItems.AddRange(_filesList);
        _allSearchItems.AddRange(_directoryList);
        ApplyFilter();
        _messageForUserTools.DispatcherActionInstance(() =>
        {
            IsSearchInitializes = true;
        });
    }

    [ObservableProperty]
    public partial bool IsSearchInitializes { get; set; }

    [ObservableProperty]
    public partial bool SearchInProgress { get; set; }

    [ObservableProperty]
    public partial bool WholeWords { get; set; }

    [ObservableProperty]
    public partial bool SearchInSqlComments { get; set; }

    [ObservableProperty]
    public partial object SelectedItem { get; set; }

    [ObservableProperty]
    public partial FileTreeNodeModel? SelectedTreeItem { get; set; }

    private const int SearchFileSizeLimit = 10 * 1024 * 1024;
    private bool SearchInFiles = false;
    private async Task DoSearchInFiles()
    {
        SearchInProgress = true;
        SearchInFiles = true;

        await Task.Run(() =>
        {
            //Parallel.ForEach(SearchItemCollections, item =>
            foreach(var item in SearchItemCollections)
            {
                try
                {
                    string ext = System.IO.Path.GetExtension(item.Name).ToLower();
                    if (item.Type != "File")
                    {
                        item.IsFounded = false;
                    }
                    else if (Path.GetFileName(item.Name).Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                    {
                        item.IsFounded = true;
                    }
                    else if (string.IsNullOrWhiteSpace(SearchText))
                    {
                        item.IsFounded = true;
                    }
                    else if (WholeWords && IGeneralApplicationData.REGISTERED_EXTENSIONS.ContainsKey(ext) && item.Length <= SearchFileSizeLimit)
                    {
                        var se = _searchInFiles.IsWholeWordInFile(item.Name, SearchText, SearchInSqlComments);
                        item.IsFounded = se;
                    }
                    else if (IGeneralApplicationData.REGISTERED_EXTENSIONS.ContainsKey(ext) && item.Length <= SearchFileSizeLimit)
                    {
                        var se = _searchInFiles.IsWordInFile(item.Name, SearchText, SearchInSqlComments);
                        item.IsFounded = se;
                    }
                    else
                    {
                        item.IsFounded = false;
                    }
                }
                catch (Exception e)
                {
                    _logToolViewModel.AddLog(e.Message, LogMessageType.error, "Error", DateTime.Now, "file search");
                }
            }
            //);
        }
    );

        SearchInProgress = false;
        ApplyFilter();
    }

    private async Task OpenDirectoryDialog()
    {
        var direcoryList = await _avaloniaSpecificHelpers.GetStorageProvider().OpenFolderPickerAsync(new FolderPickerOpenOptions() { AllowMultiple = false });
        if (direcoryList is null || direcoryList.Count < 1)
        {
            return;
        }
        var newAddedPath = direcoryList[0].Path.LocalPath;

        //OpenFolderDialog d = new OpenFolderDialog();
        //var path = await d.ShowAsync(JustyBase.Views.MainWindow.mainWindow);
        if (!string.IsNullOrWhiteSpace(newAddedPath) && Directory.Exists(newAddedPath))
        {
            var roots = new List<string>();
            if (!string.IsNullOrWhiteSpace(InitialFilePath))
            {
                roots.AddRange(InitialFilePath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            if (!roots.Exists(p => PathsEqual(p, newAddedPath)))
            {
                roots.Add(newAddedPath);
            }

            _generalApplicationData.Config.StartsFolderPaths = NormalizeRootPaths(roots);
            InitialFilePath = string.Join(';', _generalApplicationData.Config.StartsFolderPaths);

            InitTreeWithRoots();
            await RefreshFileList();
        }
    }

    private void OpenInExplorer()
    {
        if (SelectedTreeItem is FileTreeNodeModel fileNode)
        {
            _messageForUserTools.ShowOrShowInExplorerHelper(fileNode.Path);
        }
    }

    public void OpenTxtPreviewFile(string path)
    {
        string ext = Path.GetExtension(path).ToLower();
        bool supportedExtension = IGeneralApplicationData.REGISTERED_EXTENSIONS.ContainsKey(ext);
        if (supportedExtension)
        {
            var atr = File.GetAttributes(path);
            if (!atr.HasFlag(FileAttributes.Directory) && File.Exists(path))
            {
                ((IActiveDocumentManager)Factory).AddNewDocumentFromFile([path]);
            }
        }
        else if (ext == ".csv" || ext == ".txt")
        {
            ((IActiveDocumentManager)Factory).AddNewDocumentFromTxtPreview(path);
        }

        else if (!supportedExtension)
        {
            _messageForUserTools.OpenInExplorerHelper(path);
        }
    }


    private void OpenInExplorerGrid()
    {
        if (SelectedItem is SearchItem searchItem)
        {
            _messageForUserTools.ShowOrShowInExplorerHelper(searchItem.Name);
        }
    }

    private async Task CopyFullFilePathAsync()
    {
        string? path = SelectedItem is SearchItem searchItem
            ? searchItem.Name
            : SelectedTreeItem is FileTreeNodeModel fileNode
                ? fileNode.Path
                : null;

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var clipboard = _avaloniaSpecificHelpers.GetClipboard();
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(path);
        }
    }

    private async Task RemoveFileOrDirectory()
    {
        if (SelectedTreeItem is not FileTreeNodeModel selectedNode)
        {
            return;
        }
        string path = selectedNode.Path;
        var shouldDelete = await _messageForUserTools.ShowConfirmationDialogAsync(
            $"{path}\r\n will be deleted from the disk permanently",
            "Remove permanently?");

        if (shouldDelete)
        {
            try
            {
                var atr = File.GetAttributes(path);
                if (atr.HasFlag(FileAttributes.Directory))
                {
                    Directory.Delete(path);
                }
                else
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _generalApplicationData.GlobalLoggerObject.TrackError(ex, isCrash: false);
            }
        }
    }

    private void InitTreeWithRoots()
    {
        try
        {
            if (_generalApplicationData.Config.StartsFolderPaths is null)
            {
                return;
            }
            List<FileTreeNodeModel> arr = [];
            foreach (var dirPath in NormalizeRootPaths(_generalApplicationData.Config.StartsFolderPaths))
            {
                arr.Add(new FileTreeNodeModel(dirPath, true, true, _messageForUserTools, _generalApplicationData.GlobalLoggerObject));
            }
            var rootData = new FileTreeNodeModel(IGeneralApplicationData.DataDirectory, true, true, _messageForUserTools, _generalApplicationData.GlobalLoggerObject);
            arr.Add(rootData);
            HierarchicalModel.SetRoots(arr);
        }
        catch (Exception ex)
        {
            _generalApplicationData.GlobalLoggerObject.LogAndShowError(ex, _messageForUserTools);
        }
    }


    private string GetShortStart(List<string> list)
    {
        if (list is null || list.Count == 0)
        {
            return "";
        }
        var res = list[0].AsSpan();

        for (int i = 1; i < list.Count; i++)
        {
            var tmp = list[i];
            for (int j = 0; j < res.Length && j < tmp.Length; j++)
            {
                if (res[j] != tmp[j])
                {
                    res = res[..j];
                    break;
                }
            }
        }

        return res.ToString();
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathUnderRoot(string path, string root)
    {
        var normalizedPath = Path.TrimEndingDirectorySeparator(path);
        var normalizedRoot = Path.TrimEndingDirectorySeparator(root);
        if (PathsEqual(normalizedPath, normalizedRoot))
        {
            return true;
        }

        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Drops duplicate roots and roots already covered by a parent root,
    /// so the same file is not indexed twice.
    /// </summary>
    private static List<string> NormalizeRootPaths(IEnumerable<string> roots)
    {
        var distinct = roots
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Select(static p => Path.TrimEndingDirectorySeparator(p.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static p => p.Length)
            .ToList();

        List<string> result = [];
        foreach (var root in distinct)
        {
            if (result.Any(existing => IsPathUnderRoot(root, existing)))
            {
                continue;
            }

            result.Add(root);
        }

        return result;
    }

    private async Task DoInitialSearch()
    {
        var rootDirectoryList = _generalApplicationData.Config.StartsFolderPaths;
        if (rootDirectoryList is not null && rootDirectoryList.Count > 0)
        {
            await Task.Run(() =>
            {
                var roots = NormalizeRootPaths(rootDirectoryList);
                string shortStart = GetShortStart(roots);
                Stack<string> dirs = new Stack<string>(128);
                var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (string dane in roots)
                    {
                        dirs.Clear();
                        dirs.Push(dane);

                        while (dirs.Count > 0)
                        {
                            var akt = dirs.Pop();
                            string currentDir = akt;

                            if (!Directory.Exists(currentDir))
                            {
                                continue;
                            }

                            string[] subDirs = null;
                            try
                            {
                                subDirs = System.IO.Directory.GetDirectories(currentDir);
                                List<string> tmp = [];
                                for (int i = 0; i < subDirs.Length; i++)
                                {
                                    if (subDirs[i].Contains("\\."))
                                    {
                                        continue;
                                    }
                                    tmp.Add(subDirs[i]);
                                }
                                subDirs = tmp.ToArray();
                                tmp = null;
                            }
                            catch (UnauthorizedAccessException /*exc*/)
                            {
                                continue;
                            }
                            catch (DirectoryNotFoundException /*exc*/)
                            {
                                continue;
                            }
                            catch (IOException)
                            {
                                // Other IO error enumerating directory — skip
                                continue;
                            }

                            (string FullName, DateTime LastWriteTime, long Length)[] files = new DirectoryInfo(currentDir).GetFiles().OrderByDescending(f => f.LastWriteTime).Select(f => (f.FullName, f.LastWriteTime, f.Length)).ToArray();

                            foreach ((string FullName, DateTime LastWriteTime, long Length) in files)
                            {
                                string ext = System.IO.Path.GetExtension(FullName).ToLower();
                                if (ext is not null && (IGeneralApplicationData.REGISTERED_EXTENSIONS.ContainsKey(ext) || IGeneralApplicationData.ADDITIONAL_EXTENSIONS.Contains(ext))
                                )
                                {
                                    if (!seenFiles.Add(FullName))
                                    {
                                        continue;
                                    }

                                    string fileName = System.IO.Path.GetFileName(FullName);
                                    _filesList.Add(new SearchItem()
                                    {
                                        Name = FullName,
                                        ShortName = fileName,
                                        LocalPath = FullName[shortStart.Length..^(fileName.Length)],
                                        Type = "File",
                                        LastWriteTime = LastWriteTime,
                                        IsFounded = true,
                                        Length = Length
                                    });
                                }
                            }

                            foreach (string dirPath in subDirs)
                            {
                                if (!seenDirs.Add(dirPath))
                                {
                                    continue;
                                }

                                _directoryList.Add(new SearchItem()
                                {
                                    Name = dirPath,
                                    ShortName = System.IO.Path.GetFileName(dirPath),
                                    LocalPath = dirPath[shortStart.Length..],
                                    Type = "Directory",
                                    LastWriteTime = Directory.GetLastWriteTime(dirPath),
                                    IsFounded = true
                                }
                                );
                                dirs.Push(dirPath);
                            }
                        }
                    }
                }
                catch (Exception ex2)
                {
                    _messageForUserTools.ShowSimpleMessageBoxInstance(ex2);
                }
            });
        }
    }
    private void Timer_Tick(object? sender, EventArgs e)
    {
        _startupTimer.Stop();
        if (Directory.Exists(InitialFilePath))
        {
            InitTreeWithRoots();
        }
        _ = RefreshFileList();
    }

    private readonly DispatcherTimer _startupTimer = new DispatcherTimer();

    private bool FilterView(object arg)
    {
        if (arg is not SearchItem)
        {
            return false;
        }
        var item = arg as JustyBase.ViewModels.Tools.SearchItem;
        if (!SearchInFiles)
        {
            if (SearchText is null || item.ShortName.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            {
                item.IsFounded = true;
            }
            else
            {
                item.IsFounded = false;
            }
        }

        return item.IsFounded;
    }

    private static DataGridBindingDefinition CreateNodeBinding<TValue>(string name, Func<FileTreeNodeModel, TValue> getter)
    {
        return CreateBinding<HierarchicalNode, TValue>(
            name,
            node => getter((FileTreeNodeModel)node.Item));
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

    //private IControl FileCheckTemplate(FileTreeNodeModel node, INameScope ns)
    //{
    //    return new CheckBox
    //    {
    //        MinWidth = 0,
    //        [!CheckBox.IsCheckedProperty] = new Binding(nameof(FileTreeNodeModel.IsChecked)),
    //    };
    //}
    private Control FileNameTemplate(FileTreeNodeModel node, INameScope ns)
    {
        return new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
                {
                    new Image
                    {
                        [!Image.SourceProperty] = new MultiBinding
                        {
                            Bindings =
                            {
                                new Binding(nameof(node.IsDirectory)),
                                new Binding(nameof(node.IsExpanded)),
                            },
                            Converter = _folderIconConverter,
                        },
                        Margin = new Thickness(0, 0, 4, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock
                    {
                        [!TextBlock.TextProperty] = new Binding(nameof(FileTreeNodeModel.Name)),
                        VerticalAlignment = VerticalAlignment.Center,
                    }
                }
        };
    }
}

public sealed class SearchItem
{
    public string Type { get; set; }
    public string Name { get; set; }
    public string ShortName { get; set; }
    public string LocalPath { get; set; }
    public long Length { get; set; }
    public DateTime? LastWriteTime { get; set; }
    public bool IsFounded { get; set; }
}

