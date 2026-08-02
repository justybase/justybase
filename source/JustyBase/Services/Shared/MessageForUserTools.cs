using JustyBase.Common.Contracts;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace JustyBase.Helpers.Interactions;

public sealed partial class MessageForUserTools : IMessageForUserTools
{
    

    public void ShowOrShowInExplorerHelper(string path, string? argOverRide = null)
    {
        if (OperatingSystem.IsWindows() && path is not null && (File.Exists(path) || Directory.Exists(path)))
        {
            using Process showInExplorer = new();
            showInExplorer.StartInfo.FileName = "explorer";
            showInExplorer.StartInfo.Arguments = $"/select, \"{path}\"";
            if (argOverRide is not null)
            {
                showInExplorer.StartInfo.Arguments = argOverRide;
            }
            showInExplorer.Start();
        }
    }
    public void OpenInExplorerHelper(string path)
    {
        ShowOrShowInExplorerHelper(path, $"\"{path}\"");
    }


    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlashWindowEx(ref FLASHWINFO pwfi);
    public const UInt32 FLASHW_ALL = 3;

    public const UInt32 FLASHW_TIMERNOFG = 12;
    [StructLayout(LayoutKind.Sequential)]
    public struct FLASHWINFO
    {
        public UInt32 cbSize;
        public IntPtr hwnd;
        public UInt32 dwFlags;
        public UInt32 uCount;
        public UInt32 dwTimeout;
    }

    private static void FlashWindowExIfNeededByHwnd(nint hWnd)
    {
        FLASHWINFO fInfo = new FLASHWINFO();
        fInfo.cbSize = Convert.ToUInt32(Marshal.SizeOf(fInfo));
        fInfo.hwnd = hWnd;
        fInfo.dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG;
        fInfo.uCount = UInt32.MaxValue;
        fInfo.dwTimeout = 0;
        FlashWindowEx(ref fInfo);
    }

    public void ScreenShot()
    {
        if (OperatingSystem.IsWindows())
        {
            bool succes = false;
            try
            {
                using Process screenClip = new Process();
                screenClip.StartInfo.FileName = "explorer";
                screenClip.StartInfo.Arguments = "ms-screenclip:";
                screenClip.Start();
                succes = true;
            }
            catch (Exception ex)
            {
                ShowSimpleMessageBoxInstance(ex);
            }

            if (!succes)
            {
                try
                {
                    using Process screenClip = new Process();
                    screenClip.StartInfo.FileName = "SnippingTool.exe";
                    screenClip.StartInfo.Arguments = "/clip";
                    screenClip.Start();
                    succes = false;
                }
                catch (Exception ex)
                {
                    ShowSimpleMessageBoxInstance(ex);
                }
            }
        }
    }

    void IMessageForUserTools.ShowSimpleMessageBoxInstance(string messageForUser, string title)
    {
        ShowSimpleMessageBoxInstance(messageForUser, title, null);
    }

    Task<bool> IMessageForUserTools.ShowConfirmationDialogAsync(string message, string title)
    {
        return ShowConfirmationDialogAsync(message, title);
    }

    Task<string?> IMessageForUserTools.ShowAskForFileNameDialogAsync(bool gotoLine, bool showInTaskbar, bool isRename)
    {
        return ShowAskForFileNameDialogAsync(gotoLine, showInTaskbar, isRename);
    }

    Task IMessageForUserTools.ShowAboutDialogAsync()
    {
        return ShowAboutDialogAsync();
    }

    Task IMessageForUserTools.ShowFileDiffDialogAsync(string filePath, string currentText, string newText, Action reloadAction, Action keepCurrentAction)
    {
        return ShowFileDiffDialogAsync(filePath, currentText, newText, reloadAction, keepCurrentAction);
    }

}


