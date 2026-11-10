using JustyBase.PluginCommon.Enums;

namespace JustyBase.PluginCommon.Models;

/// <summary>
/// Per-engine feature switches used by the host to hide schema/editor actions a
/// driver cannot execute with valid SQL. Mirrors the capability model used by
/// the Rust/Tauri edition so both products agree on what each engine supports.
/// </summary>
/// <remarks>
/// A flag may be <see langword="true"/> only when the action produces valid SQL
/// or an explicit explanatory comment. Actions that return an empty string,
/// the legacy "not supported" sentinel or throw <see cref="NotImplementedException"/>
/// must be hidden. <see cref="SupportsIndexes"/> and <see cref="SupportsTriggers"/>
/// gate catalog browsing; the matching <c>*Ddl</c> flags gate DDL for existing objects.
/// </remarks>
public sealed record DatabaseCapabilities
{
    public bool SupportsDdl { get; init; }
    public bool SupportsDdlToClipboard { get; init; }
    public bool SupportsComments { get; init; }
    public bool SupportsExplain { get; init; }
    public bool SupportsFindUsages { get; init; }
    public bool SupportsTop100Select { get; init; }
    public bool SupportsRowCount { get; init; }
    public bool SupportsDuplicateSearch { get; init; }
    public bool SupportsDeletedRows { get; init; }
    public bool SupportsGrants { get; init; }
    public bool SupportsGroomTable { get; init; }
    public bool SupportsTableDistribution { get; init; }
    public bool SupportsTableStatistics { get; init; }
    public bool SupportsTruncateTable { get; init; }
    public bool SupportsRecreateTable { get; init; }
    public bool SupportsRecreateAllTables { get; init; }
    public bool SupportsImport { get; init; }
    public bool SupportsExport { get; init; }
    public bool SupportsIndexes { get; init; }
    public bool SupportsIndexDdl { get; init; }
    public bool SupportsPartitions { get; init; }
    public bool SupportsTriggers { get; init; }
    public bool SupportsTriggerDdl { get; init; }
    public bool SupportsTablespaces { get; init; }
    public bool SupportsSynonyms { get; init; }
    public bool SupportsExternalTables { get; init; }
    public bool SupportsSequences { get; init; }
    public bool SupportsFunctions { get; init; }
    public bool SupportsProcedures { get; init; }
    public bool SupportsAggregates { get; init; }
    public bool SupportsFluids { get; init; }
    public bool SupportsCreateTemplates { get; init; }
    public bool SupportsSchemaValueSearch { get; init; }
    public bool SupportsRenameTable { get; init; }
    public bool SupportsCreateTableFrom { get; init; }
    public bool SupportsDropObject { get; init; }
    public bool SupportsConstraints { get; init; }
}

/// <summary>
/// Engine presets. Each shipped engine has a dedicated preset; the generic
/// fallback is only used for engines without a driver (MsSqlTrusted) and for
/// <see cref="DatabaseTypeEnum.NotSupportedDatabase"/>.
/// </summary>
public static class DatabaseCapabilitiesCatalog
{
    /// <summary>Netezza: the full product surface.</summary>
    public static DatabaseCapabilities Netezza { get; } = new()
    {
        SupportsDdl = true,
        SupportsDdlToClipboard = true,
        SupportsComments = true,
        SupportsExplain = true,
        SupportsFindUsages = true,
        SupportsTop100Select = true,
        SupportsRowCount = true,
        SupportsDuplicateSearch = true,
        SupportsDeletedRows = true,
        SupportsGrants = true,
        SupportsGroomTable = true,
        SupportsTableDistribution = true,
        SupportsTableStatistics = true,
        SupportsTruncateTable = true,
        SupportsRecreateTable = true,
        SupportsRecreateAllTables = true,
        SupportsImport = true,
        SupportsExport = true,
        SupportsSynonyms = true,
        SupportsExternalTables = true,
        SupportsSequences = true,
        SupportsFunctions = true,
        SupportsProcedures = true,
        SupportsAggregates = true,
        SupportsFluids = true,
        SupportsCreateTemplates = true,
        SupportsSchemaValueSearch = true,
        SupportsRenameTable = true,
        SupportsCreateTableFrom = true,
        SupportsDropObject = true,
        SupportsConstraints = true,
    };

    /// <summary>
    /// Db2 LUW: REORG/RUNSTATS replace Netezza GROOM/GENERATE STATISTICS, and
    /// aliases/nicknames share the synonym surface. Distribution, deleted-row
    /// inspection and grant templates are not implemented by the driver yet.
    /// </summary>
    public static DatabaseCapabilities Db2 { get; } = new()
    {
        SupportsDdl = true,
        SupportsDdlToClipboard = true,
        SupportsComments = true,
        SupportsExplain = true,
        SupportsFindUsages = true,
        SupportsTop100Select = true,
        SupportsRowCount = true,
        SupportsDuplicateSearch = true,
        SupportsGroomTable = true,
        SupportsTableStatistics = true,
        SupportsTruncateTable = true,
        SupportsRecreateTable = true,
        SupportsImport = true,
        SupportsExport = true,
        SupportsIndexes = true,
        SupportsIndexDdl = true,
        SupportsPartitions = true,
        SupportsTriggers = true,
        SupportsTriggerDdl = true,
        SupportsTablespaces = true,
        SupportsSynonyms = true,
        SupportsSequences = true,
        SupportsFunctions = true,
        SupportsProcedures = true,
        SupportsAggregates = true,
        SupportsCreateTemplates = true,
        SupportsSchemaValueSearch = true,
        SupportsRenameTable = true,
        SupportsCreateTableFrom = true,
        SupportsDropObject = true,
        SupportsConstraints = true,
    };

    /// <summary>
    /// PostgreSQL: VACUUM/ANALYZE replace GROOM/GENERATE STATISTICS. The catalog
    /// query exposes tables, views, functions/procedures, sequences and indexes;
    /// synonyms, aggregates, triggers and tablespaces are not implemented by the
    /// driver yet. CTAS templates stay hidden because the base template emits
    /// Netezza's <c>DISTRIBUTE ON RANDOM</c>.
    /// </summary>
    public static DatabaseCapabilities PostgreSql { get; } = new()
    {
        SupportsDdl = true,
        SupportsDdlToClipboard = true,
        SupportsComments = true,
        SupportsExplain = true,
        SupportsFindUsages = true,
        SupportsTop100Select = true,
        SupportsRowCount = true,
        SupportsDuplicateSearch = true,
        SupportsGroomTable = true,
        SupportsTableStatistics = true,
        SupportsTruncateTable = true,
        SupportsImport = true,
        SupportsExport = true,
        SupportsIndexes = true,
        SupportsIndexDdl = true,
        SupportsPartitions = true,
        SupportsSequences = true,
        SupportsFunctions = true,
        SupportsProcedures = true,
        SupportsCreateTemplates = true,
        SupportsSchemaValueSearch = true,
        SupportsRenameTable = true,
        SupportsDropObject = true,
        SupportsConstraints = true,
    };

    /// <summary>
    /// Oracle: catalog browsing covers tables, views, procedures/functions,
    /// sequences, synonyms, indexes and triggers. The driver has no DDL for
    /// existing indexes/triggers, so only the browse groups are enabled.
    /// Comments, EXPLAIN PLAN and the sequence template are implemented.
    /// </summary>
    public static DatabaseCapabilities Oracle { get; } = new()
    {
        SupportsDdl = true,
        SupportsDdlToClipboard = true,
        SupportsComments = true,
        SupportsExplain = true,
        SupportsFindUsages = true,
        SupportsTop100Select = true,
        SupportsRowCount = true,
        SupportsDuplicateSearch = true,
        SupportsTruncateTable = true,
        SupportsImport = true,
        SupportsExport = true,
        SupportsIndexes = true,
        SupportsTriggers = true,
        SupportsSynonyms = true,
        SupportsSequences = true,
        SupportsFunctions = true,
        SupportsProcedures = true,
        SupportsCreateTemplates = true,
        SupportsSchemaValueSearch = true,
        SupportsRenameTable = true,
        SupportsDropObject = true,
        SupportsConstraints = true,
    };

    /// <summary>
    /// DuckDB: tables/views, DDL templates, EXPLAIN, import/export and the
    /// read-only catalog groups. There are no procedures, synonyms, sequences,
    /// index/trigger catalogs or statistics commands in the driver.
    /// </summary>
    public static DatabaseCapabilities DuckDb { get; } = new()
    {
        SupportsDdl = true,
        SupportsDdlToClipboard = true,
        SupportsExplain = true,
        SupportsFindUsages = true,
        SupportsTop100Select = true,
        SupportsRowCount = true,
        SupportsDuplicateSearch = true,
        SupportsTruncateTable = true,
        SupportsImport = true,
        SupportsExport = true,
        SupportsSchemaValueSearch = true,
        SupportsRenameTable = true,
        SupportsDropObject = true,
    };

    /// <summary>
    /// MySQL is an experimental stub: only tables/views, query helpers and the
    /// stateless DDL helpers (drop/rename/constraints) are usable. The
    /// create-table template, procedures and synonyms still throw in the
    /// driver, so DDL/comment/procedure/synonym groups stay hidden until P4.
    /// </summary>
    public static DatabaseCapabilities MySql { get; } = new()
    {
        SupportsExplain = true,
        SupportsFindUsages = true,
        SupportsTop100Select = true,
        SupportsRowCount = true,
        SupportsDuplicateSearch = true,
        SupportsTruncateTable = true,
        SupportsImport = true,
        SupportsExport = true,
        SupportsSchemaValueSearch = true,
        SupportsRenameTable = true,
        SupportsDropObject = true,
        SupportsConstraints = true,
    };

    public static DatabaseCapabilities Sqlite { get; } = new()
    {
        SupportsDdl = true,
        SupportsDdlToClipboard = true,
        SupportsExplain = true,
        SupportsFindUsages = true,
        SupportsTop100Select = true,
        SupportsRowCount = true,
        SupportsDuplicateSearch = true,
        SupportsGroomTable = true,
        SupportsTableStatistics = true,
        SupportsTruncateTable = true,
        SupportsImport = true,
        SupportsExport = true,
        SupportsIndexes = true,
        SupportsIndexDdl = true,
        SupportsTriggers = true,
        SupportsTriggerDdl = true,
        SupportsCreateTemplates = true,
        SupportsSchemaValueSearch = true,
        SupportsRenameTable = true,
        SupportsCreateTableFrom = true,
        SupportsDropObject = true,
    };

    /// <summary>
    /// Excel File SQL is a read-only materialization over DuckDB. Discovery,
    /// queries, EXPLAIN and export work; write actions must stay hidden.
    /// </summary>
    public static DatabaseCapabilities Excel { get; } = new()
    {
        SupportsDdl = true,
        SupportsDdlToClipboard = true,
        SupportsExplain = true,
        SupportsFindUsages = true,
        SupportsTop100Select = true,
        SupportsRowCount = true,
        SupportsDuplicateSearch = true,
        SupportsExport = true,
        SupportsSchemaValueSearch = true,
    };

    /// <summary>
    /// Microsoft Access through UCanAccess: tables/views, table DDL, query
    /// helpers and export/import. No comments, EXPLAIN, indexes or catalog
    /// groups beyond tables/views.
    /// </summary>
    public static DatabaseCapabilities Access { get; } = new()
    {
        SupportsDdl = true,
        SupportsDdlToClipboard = true,
        SupportsFindUsages = true,
        SupportsTop100Select = true,
        SupportsRowCount = true,
        SupportsTruncateTable = true,
        SupportsImport = true,
        SupportsExport = true,
        SupportsRenameTable = true,
        SupportsCreateTableFrom = true,
        SupportsDropObject = true,
        SupportsConstraints = true,
    };

    /// <summary>
    /// Conservative fallback for engines without a driver. Keeps the historical
    /// generic query helpers but advertises no engine-specific catalog groups.
    /// </summary>
    public static DatabaseCapabilities Generic { get; } = new()
    {
        SupportsFindUsages = true,
        SupportsTop100Select = true,
        SupportsRowCount = true,
        SupportsDuplicateSearch = true,
        SupportsTruncateTable = true,
        SupportsImport = true,
        SupportsExport = true,
        SupportsSchemaValueSearch = true,
        SupportsRenameTable = true,
    };

    /// <summary>Every engine with a driver maps to a dedicated preset.</summary>
    public static IReadOnlyDictionary<DatabaseTypeEnum, DatabaseCapabilities> ByDatabaseType { get; } =
        new Dictionary<DatabaseTypeEnum, DatabaseCapabilities>
        {
            [DatabaseTypeEnum.NetezzaSQL] = Netezza,
            [DatabaseTypeEnum.DB2] = Db2,
            [DatabaseTypeEnum.PostgreSql] = PostgreSql,
            [DatabaseTypeEnum.Oracle] = Oracle,
            [DatabaseTypeEnum.DuckDB] = DuckDb,
            [DatabaseTypeEnum.MySql] = MySql,
            [DatabaseTypeEnum.Sqlite] = Sqlite,
            [DatabaseTypeEnum.Access] = Access,
            [DatabaseTypeEnum.Excel] = Excel,
            [DatabaseTypeEnum.MsSqlTrusted] = Generic,
            [DatabaseTypeEnum.NotSupportedDatabase] = Generic,
        };

    public static DatabaseCapabilities For(DatabaseTypeEnum databaseType)
        => ByDatabaseType.TryGetValue(databaseType, out DatabaseCapabilities? capabilities)
            ? capabilities
            : Generic;
}
