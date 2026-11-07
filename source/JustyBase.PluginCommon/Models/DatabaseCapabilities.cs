using JustyBase.PluginCommon.Enums;

namespace JustyBase.PluginCommon.Models;

/// <summary>
/// Per-engine feature switches used by the host to hide schema/editor actions a
/// driver cannot execute with valid SQL. Mirrors the capability model used by
/// the Rust/Tauri edition so both products agree on what each engine supports.
/// </summary>
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
    public bool SupportsPartitions { get; init; }
    public bool SupportsTriggers { get; init; }
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
}

/// <summary>
/// Engine presets. <see cref="Generic"/> keeps the historical schema groups for
/// engines that do not have a dedicated preset yet, but drops Netezza-only
/// actions (groom/distribution/deleted rows/recreate) that cannot run there.
/// </summary>
public static class DatabaseCapabilitiesCatalog
{
    /// <summary>Actions that only Netezza executes with valid SQL.</summary>
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
        SupportsPartitions = true,
        SupportsTriggers = true,
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
    };

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
        SupportsPartitions = true,
        SupportsSynonyms = true,
        SupportsSequences = true,
        SupportsFunctions = true,
        SupportsProcedures = true,
        SupportsAggregates = true,
        SupportsCreateTemplates = true,
        SupportsSchemaValueSearch = true,
        SupportsRenameTable = true,
    };

    public static DatabaseCapabilities Sqlite { get; } = new()
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
        SupportsTriggers = true,
        SupportsSynonyms = true,
        SupportsSequences = true,
        SupportsFunctions = true,
        SupportsProcedures = true,
        SupportsAggregates = true,
        SupportsCreateTemplates = true,
        SupportsSchemaValueSearch = true,
        SupportsRenameTable = true,
        SupportsCreateTableFrom = true,
    };

    public static DatabaseCapabilities Generic { get; } = new()
    {
        SupportsDdl = true,
        SupportsDdlToClipboard = true,
        SupportsComments = true,
        SupportsExplain = true,
        SupportsFindUsages = true,
        SupportsTop100Select = true,
        SupportsRowCount = true,
        SupportsDuplicateSearch = true,
        SupportsTableStatistics = true,
        SupportsTruncateTable = true,
        SupportsImport = true,
        SupportsExport = true,
        SupportsSynonyms = true,
        SupportsSequences = true,
        SupportsFunctions = true,
        SupportsProcedures = true,
        SupportsAggregates = true,
        SupportsCreateTemplates = true,
        SupportsSchemaValueSearch = true,
        SupportsRenameTable = true,
    };

    public static DatabaseCapabilities For(DatabaseTypeEnum databaseType) => databaseType switch
    {
        DatabaseTypeEnum.NetezzaSQL => Netezza,
        DatabaseTypeEnum.DB2 => Db2,
        DatabaseTypeEnum.PostgreSql => PostgreSql,
        DatabaseTypeEnum.Sqlite => Sqlite,
        DatabaseTypeEnum.Access => Generic with { SupportsCreateTableFrom = true },
        _ => Generic,
    };
}
