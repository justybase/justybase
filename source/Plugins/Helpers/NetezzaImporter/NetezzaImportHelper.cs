using JustyBase.ImportExport.Import;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.NetezzaDdl;
using System.Data.Common;
using System.Text;

namespace JustyBase.Helpers.NetezzaImporter;

/// <summary>Avalonia host adapter over <see cref="NetezzaPipeImportExecutor"/>.</summary>
public static class NetezzaImportHelper
{
    private const char DefaultColumnSeparator = '\t';
    private const char DefaultEscapeChar = '\\';

    public static async Task NetezzaImportExecute(DbConnection conn, string tempDataDirectory, IDbImportJob importJob,
        string tableName, Action<string>? progress, string remotesource = NetezzaImportUsingOptions.DefaultRemoteSource)
    {
        var options = ImportUsingOptionsContext.Current ?? ImportUsingOptions.Default;
        char columnSeparator = string.IsNullOrEmpty(options.Delimiter)
            ? DefaultColumnSeparator
            : options.Delimiter[0];

        Encoding pipeEncoding;
        try
        {
            pipeEncoding = Encoding.GetEncoding(options.EncodingName);
        }
        catch
        {
            pipeEncoding = Encoding.UTF8;
        }

        string serverName = NetezzaPipeImportExecutor.CreatePipeName("JDE");
        var headersWithDataType = importJob.ReturnHeadersWithDataTypes(DatabaseTypeEnum.NetezzaSQL);
        bool isLineReader = importJob is IDbXMLImportJob;

        var pipeServer = NetezzaPipeImportExecutor.ServeDataReaderAsync(
            importJob.AsReader,
            serverName,
            progress,
            preparedStringsMode: isLineReader,
            delimiter: columnSeparator,
            encoding: pipeEncoding,
            rowsCount: importJob.RowsCount);

        await Task.Delay(50).ConfigureAwait(false);
        progress?.Invoke("transfer to database started");
        await Task.Run(() =>
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = NetezzaImportSql.CreateRandomDistributionTable(tableName, headersWithDataType);
                cmd.ExecuteNonQuery();
                progress?.Invoke($" {tableName} created");

                string sep2 = columnSeparator == '\t' ? "\\t" : columnSeparator.ToString();
                string encodingName = string.IsNullOrWhiteSpace(options.EncodingName) ? "utf-8" : options.EncodingName;
                cmd.CommandText = NetezzaImportEngine.BuildInsertSql(
                    tableName,
                    serverName,
                    headersWithDataType,
                    new NetezzaImportUsingOptions
                    {
                        RemoteSource = remotesource,
                        Delimiter = sep2,
                        SkipRows = 1,
                        NullValue = "",
                        EncodingName = encodingName,
                        EscapeChar = DefaultEscapeChar.ToString(),
                        TimeStyle = "24HOUR",
                        MaxErrors = 0,
                        LogDirectory = tempDataDirectory,
                        MaxRows = options.MaxRows is > 0 ? options.MaxRows : null
                    });
                cmd.ExecuteNonQuery();

                var badFilePath = Directory.EnumerateFiles(tempDataDirectory, $"{tableName}*.nzbad").FirstOrDefault();
                if (badFilePath is not null)
                    progress?.Invoke($"[ERROR] {badFilePath} created");
            }
            catch (Exception ex)
            {
                progress?.Invoke($"[ERROR] {ex.Message}");
            }
        }).ConfigureAwait(false);

        await pipeServer.ConfigureAwait(false);
    }
}
