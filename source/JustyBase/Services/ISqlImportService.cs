using Avalonia.Input.Platform;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Models.Tools;
using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Services;

public interface ISqlImportService
{
    Task ImportFromClipboardAsync(
        IClipboardService clipboardService,
        IClipboard clipboard,
        IGeneralApplicationData generalApplicationData,
        string connectionName,
        string? selectedDatabase,
        Func<string, LogMessageType, DateTime, string, LogMessage?> addLogMessage,
        Action<object, bool> insertTextAction);

    Task ImportFromFilePathAsync(
        string path,
        string connectionName,
        Action<object, bool> insertTextAction);
}
