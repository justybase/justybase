using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services;
using JustyBase.ViewModels;
using JustyBase.Views;
using JustyBase.Views.OtherDialogs;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace JustyBase.Helpers.Interactions;

public sealed partial class MessageForUserTools : IMessageForUserTools
{
    private readonly IAvaloniaSpecificHelpers _avaloniaSpecificHelpers;
    private readonly ISimpleLogger _simpleLogger;
    private readonly IServiceProvider _serviceProvider;

    public MessageForUserTools(IAvaloniaSpecificHelpers avaloniaSpecificHelpers, ISimpleLogger simpleLogger, IServiceProvider serviceProvider)
    {
        _avaloniaSpecificHelpers = avaloniaSpecificHelpers;
        _simpleLogger = simpleLogger;
        _serviceProvider = serviceProvider;
    }

    

    public void ShowSimpleMessageBoxInstance(Exception ex)
    {
        if (ex.Message.StartsWith("ORA"))
        {
            ShowSimpleMessageBoxInstance($"Message\r\n{ex.Message}", "Error", null);
        }
        else
        {
            ShowSimpleMessageBoxInstance($"Message\r\n{ex.Message}\r\nStack trace\r\n{ex.StackTrace}", "Error", null);
        }
    }

    public void ShowSimpleMessageBoxInstance(string messageForUser, string title = "Information", Window? window = null)
    {
        DispatcherActionInstance(() =>
        {
            try
            {
                new MessageWindow(messageForUser, title) { WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog(window ?? _avaloniaSpecificHelpers.GetMainWindow());
            }
            catch (Exception ex)
            {
                _simpleLogger.TrackError(ex, isCrash: false);
            }
        });
    }

    public Task<bool> ShowConfirmationDialogAsync(string message, string title = "Confirmation")
    {
        var tcs = new TaskCompletionSource<bool>();
        DispatcherActionInstance(async () =>
        {
            try
            {
                Debug.WriteLine($"[ShowConfirmationDialogAsync] Creating dialog with message length: {message?.Length ?? 0}, title: {title}");
                var dialog = new ConfirmationWindow(message, title)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                var result = await dialog.ShowDialog<bool>(_avaloniaSpecificHelpers.GetMainWindow());
                Debug.WriteLine($"[ShowConfirmationDialogAsync] Dialog result: {result}");
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShowConfirmationDialogAsync] Error: {ex.Message}\n{ex.StackTrace}");
                tcs.TrySetResult(false);
            }
        });
        return tcs.Task;
    }

    public Task<string?> ShowAskForFileNameDialogAsync(bool gotoLine = false, bool showInTaskbar = true, bool isRename = false)
    {
        var tcs = new TaskCompletionSource<string?>();
        DispatcherActionInstance(async () =>
        {
            var dialog = new AskForFileName(gotoLine, isRename)
            {
                ShowInTaskbar = showInTaskbar
            };
            await dialog.ShowDialog(_avaloniaSpecificHelpers.GetMainWindow());
            tcs.TrySetResult(dialog.ReturnedName);
        });
        return tcs.Task;
    }

    public Task ShowAboutDialogAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        DispatcherActionInstance(async () =>
        {
            try
            {
                var aboutVm = _serviceProvider.GetRequiredService<AboutViewModel>();
                await new About(aboutVm).ShowDialog(_avaloniaSpecificHelpers.GetMainWindow());
            }
            finally
            {
                tcs.TrySetResult(true);
            }
        });
        return tcs.Task;
    }

    public Task ShowFileDiffDialogAsync(string filePath, string currentText, string newText, Action reloadAction, Action keepCurrentAction)
    {
        var tcs = new TaskCompletionSource<bool>();
        DispatcherActionInstance(async () =>
        {
            try
            {
                var diffVm = new FileDiffViewModel
                {
                    FilePath = filePath,
                    ReloadAction = reloadAction,
                    KeepCurrentAction = keepCurrentAction
                };
                diffVm.SetTexts(currentText, newText);

                var diffWindow = new FileDiffWindow { DataContext = diffVm };
                await diffWindow.ShowDialog(_avaloniaSpecificHelpers.GetMainWindow());
            }
            finally
            {
                tcs.TrySetResult(true);
            }
        });
        return tcs.Task;
    }

    public Task ShowGitDiffDialogAsync(string title, string oldText, string newText)
    {
        var tcs = new TaskCompletionSource<bool>();
        DispatcherActionInstance(async () =>
        {
            try
            {
                var diffVm = new GitDiffViewModel();
                diffVm.SetContents(title, oldText, newText);
                var diffWindow = new GitDiffWindow { DataContext = diffVm };
                await diffWindow.ShowDialog(_avaloniaSpecificHelpers.GetMainWindow());
            }
            finally
            {
                tcs.TrySetResult(true);
            }
        });
        return tcs.Task;
    }

    public void FlashWindowExIfNeeded()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        nint hWnd = 0;
        var res = true;
        DispatcherActionInstance(() =>
        {
            var mv = _avaloniaSpecificHelpers.GetMainWindow();
            if (mv.WindowState != WindowState.Minimized)
            {
                res = false;
            }
            else
            {
                hWnd = TopLevel.GetTopLevel(mv).TryGetPlatformHandle().Handle;
            }

            if (!res)
            {
                return;
            }
            MessageForUserTools.FlashWindowExIfNeededByHwnd(hWnd);
        });
    }

    public void DispatcherActionInstance(Action actionToDispatch)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                actionToDispatch?.Invoke();
            }
            catch (Exception ex)
            {
                _simpleLogger.TrackCrashMessagePlusOpenNotepad(ex, "Error", false);
            }
        });
    }

    public void DispatcherActionInstance(Action actionToDispatch, object dispatcherPriority)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                actionToDispatch?.Invoke();
            }
            catch (Exception ex)
            {
                _simpleLogger.TrackError(ex, isCrash: false);
            }
        }, priority: (DispatcherPriority)dispatcherPriority);
    }
}

