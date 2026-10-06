using Avalonia.Collections;
using Avalonia.Controls.DataGridHierarchical;
using Avalonia.Data;
using Avalonia.Data.Core;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
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
        LogToolViewModel logToolViewModel, Services.FileExplorer.IFileIconProvider? iconProvider = null)
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

        // F2: icons come from DI provider when available; fall back to direct load for legacy paths.
        if (iconProvider is not null
            && iconProvider.FileIcon is Avalonia.Media.Imaging.Bitmap fileIcon
            && iconProvider.FolderIcon is Avalonia.Media.Imaging.Bitmap folderIcon
            && iconProvider.FolderOpenIcon is Avalonia.Media.Imaging.Bitmap folderOpenIcon)
        {
            _folderIconConverter = new FolderIconConverter(fileIcon, folderOpenIcon, folderIcon);
        }
        else
        {
            using (var fileStream = AssetLoader.Open(new Uri("avares://JustyBase/Assets/file.png")))
            using (var folderStream = AssetLoader.Open(new Uri("avares://JustyBase/Assets/folder.png")))
            using (var folderOpenStream = AssetLoader.Open(new Uri("avares://JustyBase/Assets/folder-open.png")))
            {
                // FolderIconConverter owns these bitmaps for the lifetime of the view model.
#pragma warning disable CA2000
                var legacyFileIcon = new Bitmap(fileStream);
                var legacyFolderIcon = new Bitmap(folderStream);
                var legacyFolderOpenIcon = new Bitmap(folderOpenStream);
#pragma warning restore CA2000

                _folderIconConverter = new FolderIconConverter(legacyFileIcon, legacyFolderOpenIcon, legacyFolderIcon);
            }
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

    /// <summary>
    /// Explorer tree roots (VS Code Explorer section). Same nodes as
    /// <see cref="HierarchicalModel"/>, exposed for a plain TreeView.
    /// </summary>
    public ObservableCollection<FileTreeNodeModel> TreeRoots { get; } = [];
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

    // ------------------------------------------------------------------
    // VS Code Explorer tree: open / create / rename / refresh / navigate.
    // ------------------------------------------------------------------
    [RelayCommand]
    private void OpenSelectedNode() => ActivateTreeNode(SelectedTreeItem);

    [RelayCommand]
    private async Task RefreshTreeAsync()
    {
        var roots = TreeRoots.ToArray();
        if (roots.Length == 0)
        {
            InitTreeWithRoots();
            return;
        }

        await Task.WhenAll(roots.Select(static root => root.RefreshAsync()));
    }

    [RelayCommand]
    private void CollapseAllTree()
    {
        foreach (var root in TreeRoots)
        {
            CollapseTreeNode(root);
        }
    }

    [RelayCommand]
    private async Task CreateFileAsync() => await CreateTreeEntryAsync(folder: false);

    [RelayCommand]
    private async Task CreateFolderAsync() => await CreateTreeEntryAsync(folder: true);

    [RelayCommand]
    private async Task RenameNodeAsync()
    {
        if (SelectedTreeItem is not FileTreeNodeModel node)
        {
            return;
        }

        string? newName = await _messageForUserTools.ShowAskForFileNameDialogAsync(isRename: true);
        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        string? directory = Path.GetDirectoryName(node.Path);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        string target = Path.Combine(directory, newName.Trim());
        if (PathsEqual(node.Path, target))
        {
            return;
        }

        try
        {
            if (node.IsDirectory)
            {
                Directory.Move(node.Path, target);
            }
            else
            {
                File.Move(node.Path, target);
            }

            node.Path = target;
            node.Name = newName.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance(ex.Message, "Rename");
        }
    }

    /// <summary>VS Code Up/Down in the tree.</summary>
    public void StepTreeSelection(int delta)
    {
        var visible = FlattenVisibleTree();
        if (visible.Count == 0)
        {
            return;
        }

        int index = visible.FindIndex(entry => ReferenceEquals(entry.Node, SelectedTreeItem));
        int next = index < 0
            ? (delta > 0 ? 0 : visible.Count - 1)
            : Math.Clamp(index + delta, 0, visible.Count - 1);
        SelectedTreeItem = visible[next].Node;
    }

    /// <summary>VS Code Left in the tree: collapse, otherwise go to parent.</summary>
    public void TreeLeft()
    {
        if (SelectedTreeItem is not FileTreeNodeModel node)
        {
            return;
        }

        if (node.IsDirectory && node.IsExpanded)
        {
            node.IsExpanded = false;
            return;
        }

        FileTreeNodeModel? parent = FindTreeParent(node);
        if (parent is not null)
        {
            SelectedTreeItem = parent;
        }
    }

    /// <summary>VS Code Right in the tree: expand a collapsed folder.</summary>
    public void TreeRight()
    {
        if (SelectedTreeItem is FileTreeNodeModel { IsDirectory: true, IsExpanded: false } node)
        {
            node.IsExpanded = true;
        }
    }

    private void ActivateTreeNode(FileTreeNodeModel? node)
    {
        if (node is null)
        {
            return;
        }

        if (node.IsDirectory)
        {
            node.IsExpanded = !node.IsExpanded;
        }
        else
        {
            OpenTxtPreviewFile(node.Path);
        }
    }

    private async Task CreateTreeEntryAsync(bool folder)
    {
        string? baseDir = ResolveTreeTargetDirectory();
        if (string.IsNullOrEmpty(baseDir))
        {
            return;
        }

        string? name = await _messageForUserTools.ShowAskForFileNameDialogAsync();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        string full = Path.Combine(baseDir, name.Trim());
        try
        {
            if (folder)
            {
                Directory.CreateDirectory(full);
            }
            else
            {
                using (File.Create(full))
                {
                }

                OpenTxtPreviewFile(full);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance(ex.Message, folder ? "New folder" : "New file");
        }
    }

    private string? ResolveTreeTargetDirectory()
    {
        if (SelectedTreeItem is FileTreeNodeModel node)
        {
            if (node.IsDirectory)
            {
                return node.Path;
            }

            string? parent = Path.GetDirectoryName(node.Path);
            if (!string.IsNullOrEmpty(parent))
            {
                return parent;
            }
        }

        return TreeRoots.FirstOrDefault()?.Path;
    }

    private static void CollapseTreeNode(FileTreeNodeModel node)
    {
        node.IsExpanded = false;
        if (!node.AreChildrenLoaded)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            CollapseTreeNode(child);
        }
    }

    private List<(FileTreeNodeModel Node, FileTreeNodeModel? Parent)> FlattenVisibleTree()
    {
        var list = new List<(FileTreeNodeModel, FileTreeNodeModel?)>();
        foreach (var root in TreeRoots)
        {
            AddVisibleTreeNode(root, null, list);
        }

        return list;
    }

    private static void AddVisibleTreeNode(
        FileTreeNodeModel node,
        FileTreeNodeModel? parent,
        List<(FileTreeNodeModel Node, FileTreeNodeModel? Parent)> list)
    {
        list.Add((node, parent));
        if (!node.IsExpanded || !node.AreChildrenLoaded)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            AddVisibleTreeNode(child, node, list);
        }
    }

    private FileTreeNodeModel? FindTreeParent(FileTreeNodeModel node)
    {
        foreach (var entry in FlattenVisibleTree())
        {
            if (ReferenceEquals(entry.Node, node))
            {
                return entry.Parent;
            }
        }

        return null;
    }

    [RelayCommand]
    private async Task CopyPathAsync(string? path)
    {
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

    [RelayCommand]
    private void OpenPathInExplorer(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _messageForUserTools.ShowOrShowInExplorerHelper(path);
    }

    private const int SearchFileSizeLimit = 10 * 1024 * 1024;
    private bool SearchInFiles;
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
                    string ext = System.IO.Path.GetExtension(item.Name).ToLowerInvariant();
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
        string ext = Path.GetExtension(path).ToLowerInvariant();
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

            foreach (var old in TreeRoots)
            {
                old.Dispose();
            }

            TreeRoots.Clear();
            SelectedTreeItem = null;
            foreach (var root in arr)
            {
                TreeRoots.Add(root);
                root.IsExpanded = true;
            }
        }
        catch (Exception ex)
        {
            _generalApplicationData.GlobalLoggerObject.LogAndShowError(ex, _messageForUserTools);
        }
    }


    private static string GetShortStart(List<string> list)
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
            // F1: enumerate on background thread, mutate UI collections only on UI thread.
            // No ConfigureAwait(false) here on purpose — continuation must resume on UI thread
            // because _filesList/_directoryList are bound to the view.
            var snapshot = await Task.Run(() =>
            {
                var files = new List<SearchItem>();
                var dirs = new List<SearchItem>();
                try
                {
                    var roots = NormalizeRootPaths(rootDirectoryList);
                    string shortStart = GetShortStart(roots);
                    Stack<string> stack = new Stack<string>(128);
                    var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string dane in roots)
                    {
                        stack.Clear();
                        stack.Push(dane);

                        while (stack.Count > 0)
                        {
                            var akt = stack.Pop();
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

                            (string FullName, DateTime LastWriteTime, long Length)[] foundFiles = new DirectoryInfo(currentDir).GetFiles().OrderByDescending(f => f.LastWriteTime).Select(f => (f.FullName, f.LastWriteTime, f.Length)).ToArray();

                            foreach ((string FullName, DateTime LastWriteTime, long Length) in foundFiles)
                            {
                                string ext = System.IO.Path.GetExtension(FullName).ToLowerInvariant();
                                if (ext is not null && (IGeneralApplicationData.REGISTERED_EXTENSIONS.ContainsKey(ext) || IGeneralApplicationData.ADDITIONAL_EXTENSIONS.Contains(ext))
                                )
                                {
                                    if (!seenFiles.Add(FullName))
                                    {
                                        continue;
                                    }

                                    string fileName = System.IO.Path.GetFileName(FullName);
                                    files.Add(new SearchItem()
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

                                dirs.Add(new SearchItem()
                                {
                                    Name = dirPath,
                                    ShortName = System.IO.Path.GetFileName(dirPath),
                                    LocalPath = dirPath[shortStart.Length..],
                                    Type = "Directory",
                                    LastWriteTime = Directory.GetLastWriteTime(dirPath),
                                    IsFounded = true
                                }
                                );
                                stack.Push(dirPath);
                            }
                        }
                    }
                }
                catch (Exception ex2)
                {
                    return (files, dirs, error: ex2);
                }

                return (files, dirs, error: (Exception?)null);
            });

            if (snapshot.error is not null)
            {
                _messageForUserTools.ShowSimpleMessageBoxInstance(snapshot.error);
                return;
            }

            foreach (var f in snapshot.files)
            {
                _filesList.Add(f);
            }

            foreach (var d in snapshot.dirs)
            {
                _directoryList.Add(d);
            }
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

    private StackPanel FileNameTemplate(FileTreeNodeModel node, INameScope ns)
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
                                CompiledBindingFactory.OneWay<FileTreeNodeModel, bool>(
                                    nameof(FileTreeNodeModel.IsDirectory),
                                    item => item.IsDirectory),
                                CompiledBindingFactory.OneWay<FileTreeNodeModel, bool>(
                                    nameof(FileTreeNodeModel.IsExpanded),
                                    item => item.IsExpanded),
                            },
                            Converter = _folderIconConverter,
                        },
                        Margin = new Thickness(0, 0, 4, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock
                    {
                        [!TextBlock.TextProperty] = CompiledBindingFactory.OneWay<FileTreeNodeModel, string>(
                            nameof(FileTreeNodeModel.Name),
                            item => item.Name),
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
