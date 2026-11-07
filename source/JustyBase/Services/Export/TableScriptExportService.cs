using System.Data.Common;
using JustyBase.Common.Contracts;
using JustyBase.Common.Tools;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services.Documents;

namespace JustyBase.Services.Export;

public sealed record TableScriptExportResult(long RowsExported, string FilePath);

/// <summary>
/// Exports one table as a self-contained script: the exact DDL produced by the
/// driver followed by INSERT statements streamed from SELECT *.
/// </summary>
public sealed class TableScriptExportService
{
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IDatabaseServiceResolver _databaseServiceResolver;

    public TableScriptExportService(
        IGeneralApplicationData generalApplicationData,
        IDatabaseServiceResolver databaseServiceResolver)
    {
        _generalApplicationData = generalApplicationData;
        _databaseServiceResolver = databaseServiceResolver;
    }

    public async Task<TableScriptExportResult> ExportAsync(
        string connectionName,
        string database,
        string schema,
        string table,
        string filePath,
        Action<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var databaseService = await Task.Run(
                () => _databaseServiceResolver.GetDatabaseService(_generalApplicationData, connectionName),
                cancellationToken)
            .ConfigureAwait(false);
        if (databaseService is null)
        {
            throw new InvalidOperationException($"Connection '{connectionName}' is not available.");
        }

        string ddl = await databaseService.GetCreateTableText(database, schema, table).ConfigureAwait(false);
        string qualified = string.Join(
            ".",
            new[] { database, schema, table }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(databaseService.QuoteNameIfNeeded));

        long rows = await Task.Run(async () =>
        {
            using DbConnection connection = databaseService.GetConnection(database);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var command = databaseService.CreateCommandFromConnection(connection);
            command.CommandText = $"SELECT * FROM {qualified}";
            command.CommandTimeout = 0;
            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await ResultExportWriters.WriteSqlScriptAsync(
                reader,
                filePath,
                qualified,
                databaseService.DatabaseType,
                ddl,
                progress: progress,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        return new TableScriptExportResult(rows, filePath);
    }
}
