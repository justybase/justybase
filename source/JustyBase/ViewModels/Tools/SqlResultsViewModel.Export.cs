using System.Collections;
using System.Data.Common;
using CommunityToolkit.Mvvm.Input;
using JustyBase.Common.Contracts;
using JustyBase.Helpers;
using JustyBase.Models;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommons;
using JustyBase.Services.DataGrid;
using JustyBase.ViewModels.Documents;

namespace JustyBase.ViewModels.Tools;

/// <summary>
/// Export and toolbar actions of <see cref="SqlResultsViewModel"/>. The clipboard text
/// itself is built by <see cref="JustyBase.Services.DataGrid.IDataGridClipboardService"/>;
/// this file only orchestrates dialogs, file writers and clipboard writes.
/// </summary>
public sealed partial class SqlResultsViewModel
{
    [RelayCommand]
    private async Task ExportAllResults()
    {
        string randomName = await _messageForUserTools.ShowAskForFileNameDialogAsync();

        var filePathToExport = Path.Combine(IGeneralApplicationData.DataDirectory, $"{randomName}{_resultHelperService.DefaultExcelExtension}");
        List<(DbDataReader, string)> listOfResults = [];

        if (!_generalApplicationData.TryGetDocumentById(this.RelatedSqlDocumentId, out var docRes))
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance("ExportAllResults - error", "Warning");
            return;
        }

        List<SqlResultsViewModel> results = _activeDocumentManager.GetDocumentResults(docRes.HotDocumentViewModelAsT<SqlDocumentViewModel>());
        if (results is null || results.Count == 0)
        {
            return;
        }
        foreach (var item in results)
        {
            listOfResults.Add((new DBReaderWithMessagesTable(item.CurrentResultsTable, null), item.SQL));
        }

        if (listOfResults.Count > 0)
        {
            try
            {
                await _resultHelperService.CreateXlsbOrXlsxFile(filePathToExport, listOfResults);
            }
            finally
            {
                foreach (var (reader, _) in listOfResults)
                    reader.Dispose();
            }
            try
            {
                await _avaloniaSpecificHelpers.CopyFileToClipboard(filePathToExport);
            }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
                {
                    _simpleLogger.LogAndShowError(ex, _messageForUserTools);
                }
        }
    }

    [RelayCommand]
    private async Task ActionFromButton(string whatAction)
    {
        bool canceled = false;
        ResultGridToolbarAction action = _actionRoutingService.Resolve(whatAction);
        if (_actionRoutingService.RequiresTableReader(action))
        {
            using var rdr = new DBReaderWithMessagesTable(CurrentResultsTable, null);
            if (CurrentResultsTable.TypeCodes is null)
            {
                ShowFlyoutCommand?.Execute("ERROR");
                return;
            }

            string randomName = StringExtension.RandomSuffix();

            string filePathToExport = Path.Combine(IGeneralApplicationData.DataDirectory, $"{randomName}{_resultHelperService.DefaultExcelExtension}");
            if (action is ResultGridToolbarAction.CopyAsCsvClipboard or ResultGridToolbarAction.CopyAsCsvClipboardHeaders)
            {
                using StringWriter stringWriter = new StringWriter();

                bool headers = action == ResultGridToolbarAction.CopyAsCsvClipboardHeaders;
                try
                {
                    _resultHelperService.CreateCsvFile(stringWriter, rdr, headers);
                }
                catch (Exception ex)
                {
                    _simpleLogger.LogAndShowError(ex, _messageForUserTools);
                }

                await _clipboardService.SetTextAsync(stringWriter.ToString());
            }
            else if (action is ResultGridToolbarAction.CopyAsExcelFileClipboard
                     or ResultGridToolbarAction.OpenAsExcelFileClipboard
                     or ResultGridToolbarAction.SaveAsExcelFile)
            {
                if (action == ResultGridToolbarAction.CopyAsExcelFileClipboard)
                {
                    randomName = await _messageForUserTools.ShowAskForFileNameDialogAsync(showInTaskbar: false);
                    filePathToExport = Path.Combine(IGeneralApplicationData.DataDirectory, $"{randomName}{_resultHelperService.DefaultExcelExtension}");
                    if (String.IsNullOrWhiteSpace(randomName))
                    {
                        canceled = true;
                    }
                }
                else if (action == ResultGridToolbarAction.SaveAsExcelFile)
                {
                    var saveFile = await _avaloniaSpecificHelpers.GetStorageProvider().SaveFilePickerAsync(
                        new FilePickerSaveOptions()
                        {
                            FileTypeChoices =
                            [
                                new("excel file") { Patterns = [".xlsb"] },
                                new("excel file") { Patterns = [".xlsx"] },
                                new("csv file") { Patterns = [".csv"] },
                                new("zstd csv file") { Patterns = [".csv.zst"] },
                                new("parquet file") { Patterns = [".parquet"] },
                                new("zipped csv file") { Patterns = [".csv.zip"] },
                                new("brotli csv file") { Patterns = [".csv.br"] },
                                new("gzip csv file") { Patterns = [".csv.gz"] },
                            ],
                            DefaultExtension = ".xlsb",
                            ShowOverwritePrompt = true
                        }
                    );

                    if (saveFile is null)
                    {
                        return;
                    }
                    filePathToExport = saveFile.Path.LocalPath;
                }

                if (string.IsNullOrWhiteSpace(filePathToExport))
                {
                    return;
                }

                if (!canceled)
                {
                    await _resultHelperService.CreateExcelFileAsync(filePathToExport, rdr, SQL);

                    if (action == ResultGridToolbarAction.CopyAsExcelFileClipboard)
                    {
                        try
                        {
                            await _avaloniaSpecificHelpers.CopyFileToClipboard(filePathToExport);
                        }
                        catch (Exception ex)
                        {
                            _simpleLogger.TrackError(ex, isCrash: false);
                        }
                    }
                    else if (action == ResultGridToolbarAction.OpenAsExcelFileClipboard)
                    {
                        _messageForUserTools.OpenInExplorerHelper(filePathToExport.Replace("/", "\\").Replace("\\\\", "\\"));
                    }
                }
            }
            else if (action == ResultGridToolbarAction.CopyAsHtml)
            {
                using var dataTransfer = new DataTransfer();
                DataFormat<byte[]> _customBinaryDataFormat = DataFormat.CreateBytesPlatformFormat("HTML Format");
                dataTransfer.Add(DataTransferItem.Create(_customBinaryDataFormat, CopyHtmlOrTextClipboard.GetHtmlBytesOfTable(CurrentResultsTable)));
                await _avaloniaSpecificHelpers.GetClipboard().SetDataAsync(dataTransfer);
            }
            else if (action == ResultGridToolbarAction.CopySelectedCellsCurrentColumn)
            {
                await _clipboardService.SetTextAsync(_dataGridClipboardService.BuildSelectedCellsColumnText(SelectedColumnCells));
            }
            else if (action == ResultGridToolbarAction.CopySelectedCellsCurrentColumnRange)
            {
                try
                {
                    if (CurrentResultsTable?.Headers is { Count: > 0 } headers
                        && SelectedItems is not null
                        && PrevCols.Count >= 2
                        && PrevCols.TryDequeue(out int prev1)
                        && PrevCols.TryDequeue(out int prev2))
                    {
                        string rangeText = _dataGridClipboardService.BuildSelectedRangeText(
                            headers,
                            SelectedItems.OfType<TableRow>(),
                            prev1,
                            prev2);

                        await _clipboardService.SetTextAsync(rangeText);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
                {
                    _simpleLogger.LogAndShowError(ex, _messageForUserTools);
                }
            }
            else if (action == ResultGridToolbarAction.CopyRowValues)
            {
                IList selectedRows = SelectedItems;
                if (selectedRows.Count == 1 && selectedRows[0] is TableRow selectedRow)
                {
                    await _clipboardService.SetTextAsync(_dataGridClipboardService.BuildRowValuesText(selectedRow));
                }
            }
        }

        if (canceled)
        {
            return;
        }


        ShowFlyoutCommand?.Execute(whatAction);
    }
}
