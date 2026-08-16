namespace JustyBase.SqliteDriver.Metadata;

public sealed record SqliteCatalogInfo(string Name, string? FilePath, int Sequence);

public sealed record SqliteIndexColumn(
    int Sequence,
    int TableColumn,
    string? Name,
    bool Descending,
    string? Collation,
    bool IsKey,
    string? Expression = null);

public sealed record SqliteIndexDefinition(
    string Database,
    string Name,
    string TableName,
    bool IsUnique,
    bool IsPartial,
    string Origin,
    string? Sql,
    IReadOnlyList<SqliteIndexColumn> Columns);

public sealed record SqliteForeignKeyDefinition(
    string TableName,
    int Id,
    int Sequence,
    string? ReferencedTable,
    string? FromColumn,
    string? ToColumn,
    string? OnUpdate,
    string? OnDelete,
    string? Match);

public sealed record SqliteTableInfo(
    string Database,
    string Name,
    string Type,
    int ColumnCount,
    bool WithoutRowId,
    bool Strict,
    string? Module,
    string? Sql);

public sealed record SqliteSchemaSnapshot(
    string Database,
    IReadOnlyList<SqliteTableInfo> Tables,
    IReadOnlyList<SqliteIndexDefinition> Indexes,
    IReadOnlyDictionary<string, IReadOnlyList<SqliteForeignKeyDefinition>> ForeignKeys,
    DateTimeOffset LoadedAt)
{
    public static SqliteSchemaSnapshot Empty(string database) => new(
        database,
        Array.Empty<SqliteTableInfo>(),
        Array.Empty<SqliteIndexDefinition>(),
        new Dictionary<string, IReadOnlyList<SqliteForeignKeyDefinition>>(StringComparer.OrdinalIgnoreCase),
        DateTimeOffset.UtcNow);
}
