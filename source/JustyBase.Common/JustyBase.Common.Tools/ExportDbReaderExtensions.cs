using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommons;
using JustyBase.Common.Tools.ImportHelpers;
using JustyBase.ImportExport.Export;
using K4os.Compression.LZ4.Streams;
using SpreadSheetTasks;
using Sylvan.Data.Csv;
using System.Data.Common;
using System.IO.Compression;
using System.Text;

namespace JustyBase.Common.Tools;

/// <summary>
/// Avalonia export façade. CSV / Parquet / gzip / zip SoT lives in ImportExport;
/// Excel and host-only codecs (LZ4, Brotli, Zstd) remain here.
/// </summary>
public static class ExportDbReaderExtensions
{
    public static void HandleExcelOutput(this DbDataReader rdr, string filePathToExport, string sql,
        string? docPropertyProgramName, Action<int>? progressAction)
    {
        ExcelWriter excelFile;
        if (filePathToExport.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            excelFile = new XlsxWriter(filePathToExport)
            {
                SuppressYear1000Dates = true,
            };
        }
        else
        {
            excelFile = new XlsbWriter(filePathToExport)
            {
                SuppressYear1000Dates = true,
            };
        }

        if (docPropertyProgramName is not null)
        {
            excelFile.DocPropertyProgramName = docPropertyProgramName;
        }

        try
        {
            int i = 1;
            do
            {
                if (rdr.FieldCount != -1)
                {
                    excelFile.AddSheet($"Sheet{i}");
                    excelFile.On10k += progressAction;
                    excelFile.WriteSheet(rdr, doAutofilter: true);
                    excelFile.AddSheet($"SQL{i}", hidden: true);
                    excelFile.WriteSheet(sql.GetSqLParts());
                    i++;
                }
            } while (rdr.NextResult());
        }
        finally
        {
            excelFile.Dispose();
        }

    }

#pragma warning disable CA2000
    public static async Task<string> HandleCsvOrParquetOutput(this DbDataReader rdr, string filePathToExport, AdvancedExportOptions? opt, Action<long>? progressAction)
    {
        string finalFilePath = filePathToExport;
        int resultNumber = 1;
        do
        {
            if (rdr.FieldCount != -1)
            {
                string filePathToExportX = filePathToExport;
                if (resultNumber > 1)
                {
                    filePathToExportX += $"_{resultNumber}";
                }

                if (opt is null)
                {
                    opt = new AdvancedExportOptions();
                    if (filePathToExport.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        opt.CompresionType = CompressionEnum.Zip;
                        filePathToExportX = filePathToExportX[..^4];
                    }
                    if (filePathToExport.EndsWith(".br", StringComparison.OrdinalIgnoreCase))
                    {
                        opt.CompresionType = CompressionEnum.Brotli;
                        filePathToExportX = filePathToExportX[..^3];
                    }
                    if (filePathToExport.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                    {
                        opt.CompresionType = CompressionEnum.Gzip;
                        filePathToExportX = filePathToExportX[..^3];
                    }
                    if (filePathToExport.EndsWith(".zst", StringComparison.OrdinalIgnoreCase))
                    {
                        opt.CompresionType = CompressionEnum.Zstd;
                        filePathToExportX = filePathToExportX[..^4];
                    }
                    opt.LineDelimiter = "\r\n";
                    opt.Delimiter = '|';
                    opt.Encod = Encoding.UTF8;
                    opt.Header = true;
                }

                StreamWriter streamWriter = null!;
                Action additionalAction = null!;
                try
                {
                    if (opt.CompresionType is CompressionEnum.Gzip or CompressionEnum.Zip or CompressionEnum.None)
                    {
                        SharedCompressionKind kind = opt.CompresionType switch
                        {
                            CompressionEnum.Gzip => SharedCompressionKind.Gzip,
                            CompressionEnum.Zip => SharedCompressionKind.Zip,
                            _ => SharedCompressionKind.None
                        };
                        var opened = CompressedExportStreams.Open(filePathToExportX, kind, opt.Encod);
                        streamWriter = opened.Writer;
                        finalFilePath = opened.FinalFilePath;
                        additionalAction = opened.Dispose;
                    }
                    else if (opt.CompresionType == CompressionEnum.L4z)
                    {
                        finalFilePath = filePathToExportX + ".lz4";
                        var fileStream = File.Open(finalFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                        var helperStream = LZ4Stream.Encode(fileStream);
                        streamWriter = new StreamWriter(helperStream);
                        additionalAction = () =>
                        {
                            streamWriter.Dispose();
                            fileStream.Dispose();
                            helperStream.Dispose();
                        };
                    }
                    else if (opt.CompresionType == CompressionEnum.Brotli)
                    {
                        finalFilePath = filePathToExportX + ".br";
                        var fileStream = File.Open(finalFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                        var helperStream = new BrotliStream(fileStream, CompressionLevel.Optimal);
                        streamWriter = new StreamWriter(helperStream);

                        additionalAction = () =>
                        {
                            streamWriter.Dispose();
                            fileStream.Dispose();
                            helperStream.Dispose();
                        };
                    }
                    else if (opt.CompresionType == CompressionEnum.Zstd)
                    {
                        finalFilePath = filePathToExportX + ".zst";
                        var fileStream = File.Open(finalFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                        var helperStream = new ZstdSharp.CompressionStream(fileStream);
                        streamWriter = new StreamWriter(helperStream);

                        additionalAction = () =>
                        {
                            streamWriter.Dispose();
                            fileStream.Dispose();
                            helperStream.Dispose();
                        };
                    }
                    else
                    {
                        streamWriter = opt.Encod is not null
                            ? new StreamWriter(filePathToExportX, append: false, encoding: opt.Encod)
                            : new StreamWriter(filePathToExportX);
                        additionalAction = () => streamWriter.Dispose();
                    }


                    if (filePathToExport.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase))
                    {
                        using var dbReaderMsgs = new DBReaderWithMessages(rdr, progressAction);
                        var parquetWritter = new ParquetExportWriter(dbReaderMsgs);
                        await parquetWritter.CreateFile(streamWriter.BaseStream).ConfigureAwait(false);
                    }
                    else if (opt.CompresionType == CompressionEnum.None)
                    {
                        CsvExportWriter.WriteFromDataReader(
                            streamWriter,
                            rdr,
                            new JustyBase.ImportExport.Export.ExportOptions(
                                Delimiter: opt.Delimiter,
                                NewLine: string.IsNullOrEmpty(opt.LineDelimiter) ? "\r\n" : opt.LineDelimiter,
                                IncludeHeaders: opt.Header,
                                Encoding: opt.Encod),
                            progressAction);
                    }
                    else
                    {
                        using var csvWriter = CsvDataWriter.Create(streamWriter, new CsvDataWriterOptions()
                        {
                            NewLine = opt.LineDelimiter,
                            Delimiter = opt.Delimiter,
                            WriteHeaders = opt.Header
                        });
                        using var dbReaderMsgs = new DBReaderWithMessages(rdr, progressAction);
                        csvWriter.Write(dbReaderMsgs);
                    }
                }
                finally
                {
                    additionalAction.Invoke();
                }

                resultNumber++;
            }
        } while (rdr.NextResult());


        return finalFilePath;
    }

}
#pragma warning restore CA2000
