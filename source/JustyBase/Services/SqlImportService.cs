using Avalonia.Input.Platform;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Common.Tools.ImportHelpers;
using JustyBase.Common.Tools.ImportHelpers.XML;
using JustyBase.Helpers;
using JustyBase.Helpers.Shared;
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

    public SqlImportService(
        IDatabaseServiceResolver databaseServiceResolver,
        ISimpleLogger simpleLogger,
        IMessageForUserTools messageForUserTools)
    {
        _databaseServiceResolver = databaseServiceResolver;
        _simpleLogger = simpleLogger;
        _messageForUserTools = messageForUserTools;
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

            addLogMessage("import in progress", LogMessageType.ok, DateTime.Now, "");
            string res = "";
            addLogMessage("gathering data from clipboard", LogMessageType.ok, DateTime.Now, "");
            if (formats.Contains("XML Spreadsheet"))
            {
                object xmlData = await clipboardService.GetDataAsync("XML Spreadsheet");
                if (xmlData is byte[] xmlBytes)
                {
                    res = await service.PerformImportFromXmlAsync(new DbXMLImportJob(), xmlBytes,
                        (s) =>
                        {
                            _messageForUserTools.DispatcherActionInstance
                            (
                                () => addLogMessage(s, LogMessageType.ok, DateTime.Now, "")
                            );
                        });
                }
            }
            else
            {
                string textData = await clipboardService.GetTextAsync();
                string path = Path.GetTempFileName();
                File.WriteAllText(path, textData);
                var importFrom = new ImportFromExcelFile(x => _messageForUserTools.ShowSimpleMessageBoxInstance(x), _simpleLogger)
                {
                    FilePath = path
                };

                if (!importFrom.InitImport(encoding: System.Text.Encoding.UTF8))
                {
                    addLogMessage($"IMPORT FAILED to {res}", LogMessageType.error, DateTime.Now, "");
                    return;
                }

                string randomName = StringExtension.RandomSuffix("IMP_");
                try
                {
                    await importFrom.ImportFromFileAllSteps(service.DatabaseType, service, "", randomName);
                    res = randomName;
                }
                catch (Exception ex)
                {
                    _simpleLogger.LogAndShowError(ex, _messageForUserTools);
                }

                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    importFrom.DoFileDispose();
                    File.Delete(path);
                }
                catch (UnauthorizedAccessException)
                {
                    importFrom.DoFileDispose();
                    File.Delete(path);
                }
            }
            addLogMessage($"imported to {res}", LogMessageType.ok, DateTime.Now, "");
            insertTextAction.Invoke(res, false);
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
                        await ImportFromFilePathAsync(
                            filenameX.Path.LocalPath,
                            generalApplicationData,
                            connectionName,
                            addLogMessage,
                            insertTextAction);
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
            ImportFromExcelFile importFrom = new(x => _messageForUserTools.ShowSimpleMessageBoxInstance(x), _simpleLogger)
            {
                StandardMessageAction = (msg) =>
                {
                    try
                    {
                        _messageForUserTools.DispatcherActionInstance(() => insertTextAction.Invoke("\n" + DateTime.Now + ": " + msg, true));
                    }
                    catch (Exception ex)
                    {
                        _simpleLogger.LogAndShowError(ex, _messageForUserTools);
                    }
                },
                FilePath = path
            };

            IDatabaseService? service = _databaseServiceResolver.GetDatabaseService(generalApplicationData, connectionName, delayCache: true);
            if (service is null)
            {
                return;
            }

            await importFrom.PerformFastImportFromFileAsync(service.DatabaseType, service);
        }
        catch (Exception ex)
        {
            _simpleLogger.LogAndShowError(ex, _messageForUserTools, ex.Message);
        }
    }
}
