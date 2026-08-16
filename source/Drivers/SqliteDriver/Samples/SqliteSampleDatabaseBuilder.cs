using System.Data.Common;

namespace JustyBase.SqliteDriver.Samples;

public static class SqliteSampleDatabaseBuilder
{
    public static async Task CreateAsync(
        string server,
        string database,
        SqliteSamplePack samplePack,
        IEnumerable<string> selectedObjectIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samplePack);

        if (string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException("Choose a SQLite database file before creating a sample database.");
        }

        string dataSource = Sqlite.ResolveDataSource(server, database, null);
        if (Sqlite.IsMemoryDataSource(dataSource))
        {
            throw new InvalidOperationException("A sample database requires a file-backed SQLite database, not :memory:.");
        }

        EnsureParentDirectory(dataSource);

        var selectedIds = selectedObjectIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedIds.Count == 0)
        {
            throw new InvalidOperationException("Select at least one sample database object.");
        }

        IReadOnlyList<SqliteSampleObjectDefinition> objects = ResolveObjects(samplePack, selectedIds);
        if (objects.All(item => string.IsNullOrWhiteSpace(item.CreateSql)))
        {
            throw new InvalidOperationException("The selected sample does not contain any database objects to create.");
        }

        var service = new Sqlite(string.Empty, string.Empty, string.Empty, server, database, 30);
        await using DbConnection connection = service.GetConnection(null, pooling: false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (SqliteSampleObjectDefinition item in objects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(item.CreateSql))
                {
                    await ExecuteAsync(connection, transaction, item.CreateSql, cancellationToken).ConfigureAwait(false);
                }

                if (!string.IsNullOrWhiteSpace(item.SeedSql))
                {
                    await ExecuteAsync(connection, transaction, item.SeedSql, cancellationToken).ConfigureAwait(false);
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task ExecuteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<SqliteSampleObjectDefinition> ResolveObjects(
        SqliteSamplePack samplePack,
        HashSet<string> selectedIds)
    {
        var byId = samplePack.Objects.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var result = new List<SqliteSampleObjectDefinition>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string selectedId in selectedIds)
        {
            AddWithDependencies(selectedId);
        }

        return result;

        void AddWithDependencies(string id)
        {
            if (visited.Contains(id))
            {
                return;
            }

            if (!byId.TryGetValue(id, out SqliteSampleObjectDefinition? item))
            {
                throw new InvalidOperationException($"Unknown SQLite sample object '{id}'.");
            }

            if (!visiting.Add(id))
            {
                throw new InvalidOperationException($"Circular dependency in SQLite sample object '{id}'.");
            }

            foreach (string dependency in item.Dependencies)
            {
                AddWithDependencies(dependency);
            }

            visiting.Remove(id);
            visited.Add(id);
            result.Add(item);
        }
    }

    private static void EnsureParentDirectory(string dataSource)
    {
        if (dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string fullPath = Path.GetFullPath(dataSource);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
