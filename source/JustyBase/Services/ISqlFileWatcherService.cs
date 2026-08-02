using JustyBase.ViewModels.Documents;

namespace JustyBase.Services;

public interface ISqlFileWatcherService : IDisposable
{
    Action<FileChangedInfo>? OnFileChangedExternal { get; set; }
    Action<FileChangedInfo>? OnFileChangedExternalDispatcher { get; set; }
    Func<string>? GetCurrentTextFunc { get; set; }
    Func<Task<string>>? GetCurrentTextDispatcherFunc { get; set; }
    Action<Action>? UiThreadInvoker { get; set; }
    Action<string>? LoadTextFromChangedFileAction { get; set; }

    void MakeWatcher(string path);
    bool EnableRaisingEvents { get; set; }
}
