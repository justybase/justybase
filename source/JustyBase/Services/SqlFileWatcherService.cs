using JustyBase.Common.Contracts;
using JustyBase.ViewModels.Documents;

namespace JustyBase.Services;

public class SqlFileWatcherService : ISqlFileWatcherService
{
    private readonly IMessageForUserTools _messageForUserTools;
    private FileSystemWatcher _fileWatcher = new();

    public Action<FileChangedInfo>? OnFileChangedExternal { get; set; }
    public Action<FileChangedInfo>? OnFileChangedExternalDispatcher { get; set; }
    public Func<string>? GetCurrentTextFunc { get; set; }
    public Func<Task<string>>? GetCurrentTextDispatcherFunc { get; set; }
    public Action<Action>? UiThreadInvoker { get; set; }
    public Action<string>? LoadTextFromChangedFileAction { get; set; }

    public SqlFileWatcherService(IMessageForUserTools messageForUserTools)
    {
        _messageForUserTools = messageForUserTools;
    }

    public bool EnableRaisingEvents
    {
        get => _fileWatcher.EnableRaisingEvents;
        set => _fileWatcher.EnableRaisingEvents = value;
    }

    public void MakeWatcher(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }

        _fileWatcher.EnableRaisingEvents = false;
        _fileWatcher.Dispose();

        _fileWatcher = new FileSystemWatcher
        {
            Path = Path.GetDirectoryName(path) ?? string.Empty,
            Filter = Path.GetFileName(path),
            EnableRaisingEvents = true
        };

        _fileWatcher.Deleted += (s, e) => _ = Watcher_ChangedAsync(s, e);
        _fileWatcher.Changed += (s, e) => _ = Watcher_ChangedAsync(s, e);
    }

    private async Task Watcher_ChangedAsync(object sender, FileSystemEventArgs e)
    {
        var onFileChanged = OnFileChangedExternalDispatcher ?? OnFileChangedExternal;
        if (onFileChanged != null && File.Exists(e.FullPath))
        {
            var newText = await File.ReadAllTextAsync(e.FullPath);
            var currentText = GetCurrentTextDispatcherFunc is not null
                ? await GetCurrentTextDispatcherFunc.Invoke()
                : GetCurrentTextFunc?.Invoke() ?? string.Empty;

            if (currentText == newText)
            {
                return;
            }

            var fileChangedInfo = new FileChangedInfo
            {
                FilePath = e.FullPath,
                CurrentText = currentText,
                NewText = newText,
                ReloadAction = () => LoadTextFromChangedFileAction?.Invoke(e.FullPath),
                KeepCurrentAction = () => { }
            };

            if (UiThreadInvoker != null)
            {
                UiThreadInvoker.Invoke(() => onFileChanged.Invoke(fileChangedInfo));
            }
            else
            {
                onFileChanged.Invoke(fileChangedInfo);
            }
        }
        else
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance($"File was reloaded {e.FullPath}");
            await Task.Delay(100);
            if (File.Exists(e.FullPath))
            {
                LoadTextFromChangedFileAction?.Invoke(e.FullPath);
            }
        }
    }

    public void Dispose()
    {
        _fileWatcher?.Dispose();
        GC.SuppressFinalize(this);
    }
}
