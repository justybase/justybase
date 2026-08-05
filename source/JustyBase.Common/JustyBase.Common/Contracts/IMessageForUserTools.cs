namespace JustyBase.Common.Contracts;

public interface IMessageForUserTools
{
    void ShowSimpleMessageBoxInstance(Exception ex);
    void ShowSimpleMessageBoxInstance(string messageForUser, string title = "Information");
    void FlashWindowExIfNeeded();
    void DispatcherActionInstance(Action actionToDispatch);
    void DispatcherActionInstance(Action actionToDispatch, object dispatcherPriority);
    void ScreenShot();
    void ShowOrShowInExplorerHelper(string path, string? argOverRide = null);
    void OpenInExplorerHelper(string path);
    Task<bool> ShowConfirmationDialogAsync(string message, string title = "Confirmation");
    Task<string?> ShowAskForFileNameDialogAsync(bool gotoLine = false, bool showInTaskbar = true, bool isRename = false);
    Task ShowAboutDialogAsync();
    Task ShowFileDiffDialogAsync(string filePath, string currentText, string newText, Action reloadAction, Action keepCurrentAction);
    Task ShowGitDiffDialogAsync(string title, string oldText, string newText);
}
