using System.Data.Common;
using JustyBase.Common.Contracts;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.Services.Documents;

namespace JustyBase.Services.DataCopy;

public sealed record DataCopyRequest(
    string SourceConnectionName,
    string SourceDatabase,
    string SourceSchema,
    string SourceTable,
    string TargetConnectionName,
    string TargetDatabase,
    string TargetSchema,
    string TargetTable,
    bool CreateTarget,
    bool ReplaceTarget);

public sealed record DataCopyColumn(string SourceName, string TargetName, string SourceType, string TargetType, bool Nullable);

public sealed record DataCopyPreview(
    IReadOnlyList<DataCopyColumn> Columns,
    bool TargetExists,
    string? CreateTableSql,
    IReadOnlyList<string> Warnings);

public sealed record DataCopyResult(long RowsCopied, TimeSpan Elapsed);

/// <summary>
/// Streaming copies between saved connections. Values stay in the host process and
/// every destination value is bound as a parameter, so rows never pass through the UI.
/// The target table can be created from the source column types; existing tables map
/// columns by name.
/// </summary>
public sealed class DataCopyService
{
    private const int ProgressStride = 250;

    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IDatabaseServiceResolver _databaseServiceResolver;

    public DataCopyService(
        IGeneralApplicationData generalApplicationData,
        IDatabaseServiceResolver databaseServiceResolver)
    {
        _generalApplicationData = generalApplicationData;
        _databaseServiceResolver = databaseServiceResolver;
    }

    public async Task<DataCopyPreview> PreviewAsync(DataCopyRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);

        var source = Resolve(request.SourceConnectionName);
        var target = Resolve(request.TargetConnectionName);
        var sourceColumns = await Task.Run(
                () => ReadSourceColumns(source, request),
                cancellationToken)
            .ConfigureAwait(false);

        var targetColumns = await Task.Run(
                () => ReadTargetColumns(target, request),
                cancellationToken)
            .ConfigureAwait(false);

        var targetExists = targetColumns.Count > 0;
        var warnings = new List<string>();
        var mapped = MapColumns(sourceColumns, targetColumns, targetExists, warnings);
        string? createSql = null;
        if (!targetExists && request.CreateTarget)
        {
            createSql = DataCopySqlBuilder.BuildCreateTable(
                request.TargetSchema,
                request.TargetTable,
                sourceColumns,
                target.DatabaseType,
                target.QuoteNameIfNeeded);
        }
        else if (!targetExists)
        {
            warnings.Add("The target table does not exist. Enable Create target table to create it.");
        }

        return new DataCopyPreview(mapped, targetExists, createSql, warnings);
    }

    public async Task<DataCopyResult> CopyAsync(
        DataCopyRequest request,
        Action<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        if (string.Equals(request.SourceConnectionName, request.TargetConnectionName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Choose two different saved connections for a data copy.");
        }

        var source = Resolve(request.SourceConnectionName);
        var target = Resolve(request.TargetConnectionName);
        var started = System.Diagnostics.Stopwatch.StartNew();

        return await Task.Run(() =>
        {
            using var sourceConnection = source.GetConnection(request.SourceDatabase);
            sourceConnection.Open();
            using var targetConnection = target.GetConnection(request.TargetDatabase);
            targetConnection.Open();

            var sourceColumns = ReadSourceColumns(source, request, sourceConnection);
            if (sourceColumns.Count == 0)
            {
                throw new InvalidOperationException("The source query returned no columns.");
            }

            var targetColumns = ReadTargetColumns(target, request);
            var targetExists = targetColumns.Count > 0;
            var warnings = new List<string>();
            var mapped = MapColumns(sourceColumns, targetColumns, targetExists, warnings);
            if (mapped.Count == 0)
            {
                throw new InvalidOperationException(
                    "No source column matches a target column by name. Add matching columns or let the copy create the table.");
            }

            if (!targetExists)
            {
                if (!request.CreateTarget)
                {
                    throw new InvalidOperationException(
                        $"Target table {request.TargetSchema}.{request.TargetTable} does not exist.");
                }

                string createSql = DataCopySqlBuilder.BuildCreateTable(
                    request.TargetSchema,
                    request.TargetTable,
                    sourceColumns,
                    target.DatabaseType,
                    target.QuoteNameIfNeeded);
                using var create = target.CreateCommandFromConnection(targetConnection);
                create.CommandText = createSql;
                create.CommandTimeout = 120;
                create.ExecuteNonQuery();
            }

            using var transaction = targetConnection.BeginTransaction();
            if (targetExists && request.ReplaceTarget)
            {
                using var delete = target.CreateCommandFromConnection(targetConnection);
                delete.Transaction = transaction;
                delete.CommandText =
                    $"DELETE FROM {DataCopySqlBuilder.Qualify(target.QuoteNameIfNeeded, request.TargetSchema, request.TargetTable)}";
                delete.CommandTimeout = 300;
                delete.ExecuteNonQuery();
            }

            string insertSql = DataCopySqlBuilder.BuildInsert(
                request.TargetSchema,
                request.TargetTable,
                mapped,
                target.DatabaseType,
                target.QuoteNameIfNeeded);
            string sourceSql =
                $"SELECT * FROM {DataCopySqlBuilder.Qualify(source.QuoteNameIfNeeded, request.SourceSchema, request.SourceTable)}";

            using var sourceCommand = source.CreateCommandFromConnection(sourceConnection);
            sourceCommand.CommandText = sourceSql;
            sourceCommand.CommandTimeout = 0;
            using var reader = sourceCommand.ExecuteReader();

            using var insert = target.CreateCommandFromConnection(targetConnection);
            insert.CommandText = insertSql;
            insert.Transaction = transaction;
            insert.CommandTimeout = 120;

            var parameters = new DbParameter[mapped.Count];
            for (var index = 0; index < mapped.Count; index++)
            {
                var parameter = insert.CreateParameter();
                parameter.ParameterName = $"p{index}";
                insert.Parameters.Add(parameter);
                parameters[index] = parameter;
            }

            long copied = 0;
            int[] sourceOrdinals = mapped
                .Select(column => FindSourceOrdinal(reader, column.SourceName))
                .ToArray();
            try
            {
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (var index = 0; index < mapped.Count; index++)
                    {
                        var sourceIndex = sourceOrdinals[index];
                        parameters[index].Value = sourceIndex < 0 || reader.IsDBNull(sourceIndex)
                            ? DBNull.Value
                            : reader.GetValue(sourceIndex);
                    }

                    insert.ExecuteNonQuery();
                    copied++;
                    if (copied % ProgressStride == 0)
                    {
                        progress?.Invoke(copied);
                    }
                }

                transaction.Commit();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                transaction.Rollback();
                throw;
            }

            progress?.Invoke(copied);
            return new DataCopyResult(copied, started.Elapsed);
        }, cancellationToken).ConfigureAwait(false);
    }

    private IDatabaseService Resolve(string connectionName)
    {
        var service = _databaseServiceResolver.GetDatabaseService(_generalApplicationData, connectionName);
        return service ?? throw new InvalidOperationException($"Connection '{connectionName}' is not available.");
    }

    private static IReadOnlyList<DataCopyColumn> ReadSourceColumns(IDatabaseService source, DataCopyRequest request)
    {
        using var connection = source.GetConnection(request.SourceDatabase);
        connection.Open();
        return ReadSourceColumns(source, request, connection);
    }

    private static IReadOnlyList<DataCopyColumn> ReadSourceColumns(
        IDatabaseService source,
        DataCopyRequest request,
        DbConnection connection)
    {
        var columns = source.GetColumns(request.SourceDatabase, request.SourceSchema, request.SourceTable, "").ToArray();
        if (columns.Length > 0)
        {
            return columns
                .Select(column => new DataCopyColumn(
                    column.Name,
                    column.Name,
                    column.FullTypeName,
                    column.FullTypeName,
                    !column.ColumnNotNull))
                .ToArray();
        }

        using var command = source.CreateCommandFromConnection(connection);
        command.CommandText =
            $"SELECT * FROM {DataCopySqlBuilder.Qualify(source.QuoteNameIfNeeded, request.SourceSchema, request.SourceTable)} WHERE 1 = 0";
        using var reader = command.ExecuteReader(System.Data.CommandBehavior.SchemaOnly);
        var inferred = new List<DataCopyColumn>();
        for (var index = 0; index < reader.FieldCount; index++)
        {
            var type = reader.GetFieldType(index);
            inferred.Add(new DataCopyColumn(
                reader.GetName(index),
                reader.GetName(index),
                type.Name,
                type.Name,
                true));
        }

        return inferred;
    }

    private static IReadOnlyList<DatabaseColumn> ReadTargetColumns(IDatabaseService target, DataCopyRequest request)
    {
        bool exists = target
            .GetDbObjects(request.TargetDatabase, request.TargetSchema, request.TargetTable, TypeInDatabaseEnum.Table)
            .Any(item => string.Equals(item.Name, request.TargetTable, StringComparison.OrdinalIgnoreCase));
        if (!exists)
        {
            return [];
        }

        return target.GetColumns(request.TargetDatabase, request.TargetSchema, request.TargetTable, "").ToArray();
    }

    private static IReadOnlyList<DataCopyColumn> MapColumns(
        IReadOnlyList<DataCopyColumn> sourceColumns,
        IReadOnlyList<DatabaseColumn> targetColumns,
        bool targetExists,
        List<string> warnings)
    {
        if (!targetExists)
        {
            return sourceColumns;
        }

        var mapped = new List<DataCopyColumn>();
        foreach (var sourceColumn in sourceColumns)
        {
            var target = targetColumns.FirstOrDefault(
                column => string.Equals(column.Name, sourceColumn.SourceName, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                warnings.Add($"Source column {sourceColumn.SourceName} has no target column with the same name and will be skipped.");
                continue;
            }

            mapped.Add(sourceColumn with { TargetName = target.Name, TargetType = target.FullTypeName });
        }

        return mapped;
    }

    private static int FindSourceOrdinal(DbDataReader reader, string columnName)
    {
        for (var index = 0; index < reader.FieldCount; index++)
        {
            if (string.Equals(reader.GetName(index), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static void Validate(DataCopyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SourceConnectionName)
            || string.IsNullOrWhiteSpace(request.TargetConnectionName)
            || string.IsNullOrWhiteSpace(request.SourceTable)
            || string.IsNullOrWhiteSpace(request.TargetTable))
        {
            throw new ArgumentException("Source and target connections and table names are required.", nameof(request));
        }
    }
}

/// <summary>Pure SQL/type mapping for <see cref="DataCopyService"/>; unit-testable.</summary>
public static class DataCopySqlBuilder
{
    public static string Qualify(Func<string, string> quote, string schema, string table)
        => string.IsNullOrWhiteSpace(schema)
            ? quote(table)
            : $"{quote(schema)}.{quote(table)}";

    public static string BuildCreateTable(
        string schema,
        string table,
        IReadOnlyList<DataCopyColumn> columns,
        DatabaseTypeEnum targetType,
        Func<string, string> quote)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0)
        {
            throw new ArgumentException("At least one column is required.", nameof(columns));
        }

        var definitions = columns.Select(column =>
        {
            string type = MapType(column.SourceType, targetType);
            string nullability = column.Nullable ? "" : " NOT NULL";
            return $"{quote(column.SourceName)} {type}{nullability}";
        });

        return $"CREATE TABLE {Qualify(quote, schema, table)} ({string.Join(", ", definitions)});";
    }

    public static string BuildInsert(
        string schema,
        string table,
        IReadOnlyList<DataCopyColumn> columns,
        DatabaseTypeEnum targetType,
        Func<string, string> quote)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0)
        {
            throw new ArgumentException("At least one column is required.", nameof(columns));
        }

        string names = string.Join(", ", columns.Select(column => quote(column.TargetName)));
        string values = string.Join(
            ", ",
            Enumerable.Range(0, columns.Count).Select(index => targetType switch
            {
                DatabaseTypeEnum.DB2 or DatabaseTypeEnum.DuckDB => "?",
                DatabaseTypeEnum.Oracle => $":p{index}",
                _ => $"@p{index}",
            }));
        return $"INSERT INTO {Qualify(quote, schema, table)} ({names}) VALUES ({values})";
    }

    /// <summary>
    /// Maps a source column type to a portable target type. The mapping is deliberately
    /// coarse: the target engine validates the final type when the table is created.
    /// </summary>
    public static string MapType(string sourceType, DatabaseTypeEnum targetType)
    {
        string type = (sourceType ?? "").Trim().ToUpperInvariant();
        int parenthesis = type.IndexOf('(');
        string baseType = parenthesis > 0 ? type[..parenthesis].Trim() : type;

        if (baseType is "SMALLINT" or "INT2")
        {
            return "SMALLINT";
        }

        if (baseType is "INTEGER" or "INT" or "INT4" or "SERIAL" or "MEDIUMINT")
        {
            return "INTEGER";
        }

        if (baseType is "BIGINT" or "INT8" or "BIGSERIAL")
        {
            return "BIGINT";
        }

        if (baseType is "DECIMAL" or "NUMERIC" or "NUMBER" or "MONEY")
        {
            return parenthesis > 0 ? type : "DECIMAL(38, 10)";
        }

        if (baseType is "REAL" or "FLOAT4")
        {
            return "REAL";
        }

        if (baseType is "DOUBLE" or "DOUBLE PRECISION" or "FLOAT8" or "FLOAT")
        {
            return targetType == DatabaseTypeEnum.Sqlite ? "REAL" : "DOUBLE";
        }

        if (baseType is "BOOLEAN" or "BOOL" or "BIT")
        {
            return targetType switch
            {
                DatabaseTypeEnum.NotSupportedDatabase or DatabaseTypeEnum.Sqlite => "INTEGER",
                DatabaseTypeEnum.DB2 => "SMALLINT",
                _ => "BOOLEAN",
            };
        }

        if (baseType is "DATE")
        {
            return "DATE";
        }

        if (baseType is "TIME" or "TIME WITHOUT TIME ZONE")
        {
            return "TIME";
        }

        if (baseType.StartsWith("TIMESTAMP", StringComparison.Ordinal)
            || baseType is "DATETIME" or "DATETIME2" or "SMALLDATETIME")
        {
            return targetType == DatabaseTypeEnum.Sqlite ? "TEXT" : "TIMESTAMP";
        }

        if (baseType is "BLOB" or "BYTEA" or "BINARY" or "VARBINARY" or "IMAGE")
        {
            return targetType switch
            {
                DatabaseTypeEnum.PostgreSql => "BYTEA",
                DatabaseTypeEnum.Sqlite => "BLOB",
                _ => "BLOB",
            };
        }

        if (baseType is "CLOB" or "NCLOB" or "TEXT" or "NTEXT" or "LONG VARCHAR" or "XML" or "JSON" or "JSONB")
        {
            return targetType == DatabaseTypeEnum.DB2 ? "CLOB" : "TEXT";
        }

        if (baseType is "CHAR" or "NCHAR" or "CHARACTER")
        {
            return parenthesis > 0 ? type : "CHAR(1)";
        }

        if (baseType is "VARCHAR" or "NVARCHAR" or "VARCHAR2" or "CHARACTER VARYING" or "STRING")
        {
            return parenthesis > 0 ? type : "VARCHAR(4000)";
        }

        if (baseType is "UUID" or "UNIQUEIDENTIFIER")
        {
            return targetType == DatabaseTypeEnum.PostgreSql ? "UUID" : "VARCHAR(36)";
        }

        return targetType == DatabaseTypeEnum.Sqlite ? "TEXT" : "VARCHAR(4000)";
    }
}
