using Avalonia.Input.Platform;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Common.Tools.ImportHelpers;
using JustyBase.Helpers;
using JustyBase.Helpers.Shared;
using JustyBase.ImportExport.Import;
using JustyBase.Models.Tools;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommons;
using JustyBase.PluginDatabaseBase.Database;
using JustyBase.Services.Documents;

namespace JustyBase.Services;

public sealed class SqlImportService : ISqlImportService
{
    private readonly IDatabaseServiceResolver _databaseServiceResolver;
    private readonly ISimpleLogger _simpleLogger;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly IActiveDocumentManager _activeDocumentManager;

    public SqlImportService(
        IDatabaseServiceResolver databaseServiceResolver,
        ISimpleLogger simpleLogger,
        IMessageForUserTools messageForUserTools,
        IActiveDocumentManager activeDocumentManager)
    {
        _databaseServiceResolver = databaseServiceResolver;
        _simpleLogger = simpleLogger;
        _messageForUserTools = messageForUserTools;
        _activeDocumentManager = activeDocumentManager;
    }

    public async Task ImportFromClipboardAsync(
        IClipboardService clipboardService,
        IClipboard clipboard,
        IGeneralApplicationData generalApplicationData,
        string connectionName,
        string? selectedDatabase,
        Func<string, LogMessageType, DateTime, string, LogMessage?> addLogMessage,
        Action<object, bool> insertTextAction)
    {
        var formats = await clipboardService.GetFormatsAsync();

        if (formats.Contains("XML Spreadsheet") || formats.Contains("Text"))
        {
            addLogMessage("waiting for database service", LogMessageType.ok, DateTime.Now, "");

            IDatabaseService? service = await Task.Run(() =>
                _databaseServiceResolver.GetDatabaseService(generalApplicationData, connectionName, delayCache: false));
            if (service is null)
            {
                return;
            }

            if (service is INetezza && service.Connection is not null && selectedDatabase != service.Connection.Database)
            {
                service.ChangeDatabaseSpecial(service.Connection, selectedDatabase);
            }

            if (formats.Contains("XML Spreadsheet"))
            {
                addLogMessage("import in progress", LogMessageType.ok, DateTime.Now, "");
                string res = "";
                addLogMessage("gathering data from clipboard", LogMessageType.ok, DateTime.Now, "");
                object xmlData = await clipboardService.GetDataAsync("XML Spreadsheet");
                if (xmlData is byte[] xmlBytes)
                {
                    res = await service.PerformImportFromXmlAsync(new XmlImportJob(), xmlBytes,
                        (s) =>
                        {
                            _messageForUserTools.DispatcherActionInstance
                            (
                                () => addLogMessage(s, LogMessageType.ok, DateTime.Now, "")
                            );
                        });
                }

                addLogMessage($"imported to {res}", LogMessageType.ok, DateTime.Now, "");
                if (!string.IsNullOrWhiteSpace(res))
                {
                    insertTextAction.Invoke($"SELECT * FROM {res};\n", true);
                }
            }
            else
            {
                string textData = await clipboardService.GetTextAsync();
                string path = Path.GetTempFileName();
                await File.WriteAllTextAsync(path, textData);
                try
                {
                    await _activeDocumentManager.StartQuickImportAsync(path, connectionName, selectedDatabase);
                }
                finally
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
        }
        else if (formats.Contains("File"))
        {
            var filenames = await clipboard.TryGetFilesAsync();
            if (filenames is not null)
            {
                foreach (var filenameX in filenames)
                {
                    try
                    {
                        await _activeDocumentManager.StartQuickImportAsync(
                            filenameX.Path.LocalPath,
                            connectionName,
                            selectedDatabase);
                    }
                    catch (Exception ex)
                    {
                        _simpleLogger.LogAndShowError(ex, _messageForUserTools);
                    }
                }
            }
        }
    }

    public async Task ImportFromFilePathAsync(
        string path,
        IGeneralApplicationData generalApplicationData,
        string connectionName,
        Func<string, LogMessageType, DateTime, string, LogMessage?> addLogMessage,
        Action<object, bool> insertTextAction)
    {
        if (SqlDocumentViewModelHelper.NotSupportedFileExtension(path))
        {
            insertTextAction.Invoke("\n" + "not imported", true);
            return;
        }

        try
        {
            if (string.IsNullOrEmpty(connectionName))
            {
                return;
            }

            await _activeDocumentManager.StartQuickImportAsync(path, connectionName, null);
        }
        catch (Exception ex)
        {
            _simpleLogger.LogAndShowError(ex, _messageForUserTools, ex.Message);
        }
    }
}
