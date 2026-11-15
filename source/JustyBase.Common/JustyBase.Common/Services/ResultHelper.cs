using System.Data.Common;
using System.IO.Compression;
using System.Text;
using JustyBase.Common.Contracts;
using JustyBase.Common.Tools;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommons;
using JustyBase.ImportExport.Export;
using SpreadSheetTasks;
using Sylvan.Data.Csv;

namespace JustyBase.Common.Services;
public sealed class ResultHelper
{
    private readonly IGeneralApplicationData? _generalApplicationData;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly ISimpleLogger _simpleLogger;
    private readonly char _csvColumnSeparator = ';';
    private readonly string _csvRowSeparator = "\n";
    private readonly Encoding _csvEncoding = Encoding.UTF8; // ???

    public ResultHelper(IGeneralApplicationData? generalApplicationData, IMessageForUserTools messageForUserTools, ISimpleLogger simpleLogger)
    {
        _generalApplicationData = generalApplicationData;
        _messageForUserTools = messageForUserTools;
        _simpleLogger = simpleLogger;
        if (_generalApplicationData is not null)
        {
            _csvColumnSeparator = _generalApplicationData.Config.SepInExportedCsv[0];
            _csvRowSeparator = _generalApplicationData.Config.SepRowsInExportedCsv switch
            {
                "windows" => "\r\n",
                _ => "\n"
            };
        }

        if (_generalApplicationData is not null)
        {
            try
            {
                _csvEncoding = AdvancedExportOptions.ParseEnconding(_generalApplicationData.Config.EncondingName);
            }
            catch (Exception ex1)
            {
                _simpleLogger.TrackError(ex1, isCrash: false);
                _generalApplicationData.Config.EncondingName = "UTF-8";
                _generalApplicationData.SaveAppConfig();
                _csvEncoding = AdvancedExportOptions.ParseEnconding(_generalApplicationData.Config.EncondingName);
                _messageForUserTools.ShowSimpleMessageBoxInstance(ex1);
            };
        }
    }
    public void CreateCsvFile(TextWriter stringWriter, DbDataReader rdr, bool headers, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var csvWriter = CsvDataWriter.Create(stringWriter, new CsvDataWriterOptions()
        {
            NewLine = _csvRowSeparator,
            Delimiter = _csvColumnSeparator,
            WriteHeaders = headers
        });
        csvWriter.Write(rdr);
    }
    private string SheetName => _generalApplicationData?.Config?.DefaultXlsxSheetName ?? "Sheet";
    public string DefaultExcelExtension => _generalApplicationData?.Config?.UseXlsb == false ? ".xlsx" : ".xlsb";
    public async Task CreateExcelFileAsync(string filePathToExport, DbDataReader rdr, string SQL, CancellationToken cancellationToken = default)
    {
        bool IsXlsb = filePathToExport.EndsWith(".xlsb", StringComparison.OrdinalIgnoreCase);
        bool IsXlsx = filePathToExport.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
        bool IsCsvZip = filePathToExport.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        bool IsParquet = filePathToExport.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase);
        bool IsJson = filePathToExport.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        bool IsXml = filePathToExport.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
        bool IsTsv = filePathToExport.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase);
        bool IsSqlScript = filePathToExport.EndsWith(".sql", StringComparison.OrdinalIgnoreCase);
        var csvCompression = filePathToExport.GetCsvCompressionEnum();

        try
        {
            _ = new FileInfo(filePathToExport);
        }
        catch (Exception ex)
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance($"File name is not valid \r\n {ex.Message}");
            return;
        }

        DateTime exportStarted = DateTime.UtcNow;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsXlsb || IsXlsx)
            {
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ExcelWriter excelFile;
                    excelFile = IsXlsb ? new XlsbWriter(filePathToExport) { SuppressYear1000Dates = true } : new XlsxWriter(filePathToExport) { SuppressYear1000Dates = true };

                    excelFile.DocPropertyProgramName = "Justy";
                    try
                    {
                        excelFile.AddSheet($"{SheetName}1");
                        excelFile.WriteSheet(rdr, doAutofilter: true);
                        cancellationToken.ThrowIfCancellationRequested();
                        excelFile.AddSheet($"SQL", hidden: true);
                        excelFile.WriteSheet(SQL.GetSqLParts());
                    }
                    finally
                    {
                        excelFile.Dispose();
                    }
                }, cancellationToken);
            }
            else if (IsCsvZip)
            {
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var helperStream = new FileStream(filePathToExport, FileMode.Create, FileAccess.Write, FileShare.None);
                    using var archive = new ZipArchive(helperStream, ZipArchiveMode.Create, true);
                    var entry = archive.CreateEntry(Path.GetFileName(filePathToExport[..^4]));
                    Stream openedEntry = entry.Open();
                    using var streamWriter = new StreamWriter(openedEntry,_csvEncoding);
                    CreateCsvFile(streamWriter, rdr, true, cancellationToken);
                }, cancellationToken);
            }
            else if (IsParquet)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ParquetExportWriter parquetWritter = new ParquetExportWriter(rdr);
                using var fileStream = File.Open(filePathToExport, FileMode.Create, FileAccess.Write, FileShare.None);
                await parquetWritter.CreateFile(fileStream).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            else if (IsJson)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ResultExportWriters.WriteJsonAsync(rdr, filePathToExport, _csvEncoding, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            else if (IsXml)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ResultExportWriters.WriteXmlAsync(rdr, filePathToExport, _csvEncoding, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            else if (IsTsv)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ResultExportWriters.WriteTsvAsync(rdr, filePathToExport, _csvEncoding, cancellationToken: cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            else if (IsSqlScript)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string tableName = new(
                    Path.GetFileNameWithoutExtension(filePathToExport)
                        .Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_')
                        .ToArray());
                if (string.IsNullOrWhiteSpace(tableName))
                {
                    tableName = "exported_data";
                }

                await ResultExportWriters.WriteSqlScriptAsync(
                    rdr,
                    filePathToExport,
                    tableName,
                    DatabaseTypeEnum.NotSupportedDatabase,
                    encoding: _csvEncoding,
                    cancellationToken: cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            else
            {
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var fileStream = File.Open(filePathToExport, FileMode.Create, FileAccess.Write, FileShare.None);
                    Stream helperStream = csvCompression switch
                    {
                        CompressionEnum.None => fileStream,
                        CompressionEnum.Brotli => new BrotliStream(fileStream, CompressionLevel.Optimal),
                        CompressionEnum.Gzip => new GZipStream(fileStream, CompressionLevel.Optimal),
                        CompressionEnum.Zstd => new ZstandardStream(fileStream, CompressionLevel.Optimal),
                        _ => throw new NotImplementedException(),
                    };
                    using var streamWriter = new StreamWriter(helperStream, _csvEncoding);
                    CreateCsvFile(streamWriter, rdr, true, cancellationToken);
                }, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            DeletePartialExport(filePathToExport, exportStarted);
            _messageForUserTools.ShowSimpleMessageBoxInstance("Export cancelled.");
        }
        catch (Exception ex)
        {
            DeletePartialExport(filePathToExport, exportStarted);
            _simpleLogger.TrackError(ex, isCrash: false);
            _messageForUserTools.ShowSimpleMessageBoxInstance("File name is not valid ? " + "\n" + ex.Message);
            return;
        }
    }

    public async Task CreateXlsbOrXlsxFile(string filePathToExport, List<(DbDataReader, string)> listOfResults, CancellationToken cancellationToken = default)
    {
        bool IsXlsb = filePathToExport.EndsWith(".xlsb", StringComparison.OrdinalIgnoreCase);
        bool IsXlsx = filePathToExport.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

        try
        {
            _ = new FileInfo(filePathToExport);
        }
        catch (Exception ex)
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance(ex);
            return;
        }

        DateTime exportStarted = DateTime.UtcNow;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ExcelWriter excelFile;
                excelFile = IsXlsb ? new XlsbWriter(filePathToExport) { SuppressYear1000Dates = true } : new XlsxWriter(filePathToExport) { SuppressYear1000Dates = true };

                excelFile.DocPropertyProgramName = "Justy";
                try
                {
                    int i1 = 1;
                    foreach (var (rdr, SQL) in listOfResults)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        excelFile.AddSheet($"{SheetName}_{i1}");
                        excelFile.WriteSheet(rdr, doAutofilter: true);
                        excelFile.AddSheet($"SQL_{i1}", hidden: true);
                        excelFile.WriteSheet(SQL.GetSqLParts());
                        i1++;
                    }
                }
                finally
                {
                    excelFile.Dispose();
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            DeletePartialExport(filePathToExport, exportStarted);
            _messageForUserTools.ShowSimpleMessageBoxInstance("Export cancelled.");
        }
        catch (Exception ex)
        {
            DeletePartialExport(filePathToExport, exportStarted);
            _simpleLogger.TrackError(ex, isCrash: false);
            _messageForUserTools.ShowSimpleMessageBoxInstance("File name is not valid ? " + "\n" + ex.Message);
            return;
        }
    }

    private static void DeletePartialExport(string filePath, DateTime exportStarted)
    {
        try
        {
            if (File.Exists(filePath)
                && File.GetLastWriteTimeUtc(filePath) >= exportStarted)
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the next export overwrites the file.
        }
    }
}
