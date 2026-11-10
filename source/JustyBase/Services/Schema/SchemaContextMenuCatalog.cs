using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;

namespace JustyBase.Services.Schema;

public enum SchemaContextActionKind
{
    DdlToTab,
    DdlToClipboard,
    Top100,
    CountRows,
    Duplicates,
    DeletedRows,
    GrantTemplate,
    Groom,
    DistributionShow,
    DistributionChange,
    GenerateStatistics,
    EmptyTable,
    Recreate,
    ImportData,
    ExportData,
    ExportDdlAndData,
    Comment,
    Drop,
    PrimaryKey,
    Unique,
    Rename,
    Explain,
    Compare,
    FindUsages,
    ImpactAnalysis,
    CopyToConnection,
    CreateTableFrom
}

public sealed record SchemaContextMenuEntry(
    string Title,
    SchemaContextActionKind Kind,
    TypeInDatabaseEnum[] SupportedTypes,
    int SortOrder,
    string? SharedActionId = null);

/// <summary>
/// Avalonia UI adapter over shared Core schema menu SQL templates.
/// Ids are duplicated as constants so the host still compiles against older NuGet Core
/// (before <c>SchemaContextMenuCatalog.Ids</c> / <c>TryGet</c> shipped).
/// </summary>
public static class SchemaContextMenuCatalog
{
    /// <summary>Stable ids aligned with <c>JustyBase.Core.Schema.SchemaContextMenuCatalog.Ids</c>.</summary>
    public static class SharedIds
    {
        public const string Ddl = "ddl";
        public const string Select = "select";
        public const string SelectTop100 = "select_top100";
        public const string Count = "count";
        public const string Duplicates = "duplicates";
        public const string Deleted = "deleted";
        public const string Comment = "comment";
        public const string Grant = "grant";
        public const string Statistics = "statistics";
        public const string Groom = "groom";
        public const string Distribution = "distribution";
        public const string Empty = "empty";
        public const string Recreate = "recreate";
        public const string Import = "import";
        public const string Export = "export";
        public const string Drop = "drop";
    }

    public static IReadOnlyList<SchemaContextMenuEntry> Entries { get; } =
    [
        new("DDL → new tab", SchemaContextActionKind.DdlToTab, [TypeInDatabaseEnum.Table, TypeInDatabaseEnum.View, TypeInDatabaseEnum.ExternalTable], 10, SharedIds.Ddl),
        new("DDL → clipboard", SchemaContextActionKind.DdlToClipboard, [TypeInDatabaseEnum.Table, TypeInDatabaseEnum.View], 11, SharedIds.Ddl),
        new("Top 100", SchemaContextActionKind.Top100, [TypeInDatabaseEnum.Table, TypeInDatabaseEnum.View], 20, SharedIds.SelectTop100),
        new("Count rows", SchemaContextActionKind.CountRows, [TypeInDatabaseEnum.Table, TypeInDatabaseEnum.View], 21, SharedIds.Count),
        new("Duplicates", SchemaContextActionKind.Duplicates, [TypeInDatabaseEnum.Table], 22, SharedIds.Duplicates),
        new("Deleted rows", SchemaContextActionKind.DeletedRows, [TypeInDatabaseEnum.Table], 23, SharedIds.Deleted),
        new("GRANT template", SchemaContextActionKind.GrantTemplate, [TypeInDatabaseEnum.Table, TypeInDatabaseEnum.View], 30, SharedIds.Grant),
        new("GROOM…", SchemaContextActionKind.Groom, [TypeInDatabaseEnum.Table], 40, SharedIds.Groom),
        new("Show distribution chart", SchemaContextActionKind.DistributionShow, [TypeInDatabaseEnum.Table], 41, SharedIds.Distribution),
        new("Distribution code → clipboard", SchemaContextActionKind.DistributionChange, [TypeInDatabaseEnum.Table], 42),
        new("Generate statistics", SchemaContextActionKind.GenerateStatistics, [TypeInDatabaseEnum.Table], 50, SharedIds.Statistics),
        new("Empty table", SchemaContextActionKind.EmptyTable, [TypeInDatabaseEnum.Table], 51, SharedIds.Empty),
        new("Recreate", SchemaContextActionKind.Recreate, [TypeInDatabaseEnum.Table], 52, SharedIds.Recreate),
        new("Import data", SchemaContextActionKind.ImportData, [TypeInDatabaseEnum.Table], 60, SharedIds.Import),
        new("Export data", SchemaContextActionKind.ExportData, [TypeInDatabaseEnum.Table, TypeInDatabaseEnum.View], 61, SharedIds.Export),
        new("Export DDL + data…", SchemaContextActionKind.ExportDdlAndData, [TypeInDatabaseEnum.Table], 62),
        new("Add comment", SchemaContextActionKind.Comment, [TypeInDatabaseEnum.Table, TypeInDatabaseEnum.View], 31, SharedIds.Comment),
        new("Add primary key", SchemaContextActionKind.PrimaryKey, [TypeInDatabaseEnum.Table], 32),
        new("Add unique constraint", SchemaContextActionKind.Unique, [TypeInDatabaseEnum.Table], 33),
        new("Explain plan", SchemaContextActionKind.Explain, [TypeInDatabaseEnum.Table, TypeInDatabaseEnum.View], 24),
        new("Rename", SchemaContextActionKind.Rename, [TypeInDatabaseEnum.Table], 53),
        new("Create table from this", SchemaContextActionKind.CreateTableFrom, [TypeInDatabaseEnum.Table], 54),
        new("Drop object", SchemaContextActionKind.Drop, [TypeInDatabaseEnum.Table, TypeInDatabaseEnum.View, TypeInDatabaseEnum.Index, TypeInDatabaseEnum.Trigger], 80),
        new("Compare DDL with editor", SchemaContextActionKind.Compare, [TypeInDatabaseEnum.Table, TypeInDatabaseEnum.View], 81),
        new("Find usages", SchemaContextActionKind.FindUsages, [TypeInDatabaseEnum.Table, TypeInDatabaseEnum.View, TypeInDatabaseEnum.Procedure, TypeInDatabaseEnum.Function], 82),
        new("Impact analysis", SchemaContextActionKind.ImpactAnalysis, [TypeInDatabaseEnum.Table, TypeInDatabaseEnum.View, TypeInDatabaseEnum.Procedure, TypeInDatabaseEnum.Function], 83),
        new("Copy to another connection…", SchemaContextActionKind.CopyToConnection, [TypeInDatabaseEnum.Table, TypeInDatabaseEnum.View], 63),
    ];

    public static IEnumerable<SchemaContextMenuEntry> ForType(TypeInDatabaseEnum type)
        => Entries.Where(e => e.SupportedTypes.Contains(type)).OrderBy(e => e.SortOrder);

    /// <summary>Actions available for an object type on an engine with the given capabilities.</summary>
    public static IEnumerable<SchemaContextMenuEntry> ForType(TypeInDatabaseEnum type, DatabaseCapabilities capabilities)
        => ForType(type).Where(e => IsActionEnabled(GetCommandParameter(e.Kind, type), capabilities));

    /// <summary>
    /// Maps a menu command parameter onto the capability that must be present for
    /// the action to produce valid SQL on the active engine. Unknown/parameterless
    /// entries stay enabled so host-only commands (copy, refresh, show header)
    /// keep working.
    /// </summary>
    public static bool IsActionEnabled(string? commandParameter, DatabaseCapabilities capabilities) => commandParameter switch
    {
        null or "" => true,
        "DDL_TABLE" or "DDL_VIEW" or "DDL_ALL_TABLES" or "DDL_ALL_VIEWS" or "DDL_ALL_PROCEDURES" => capabilities.SupportsDdl,
        "DDL_EXTERNAL" => capabilities.SupportsDdl && capabilities.SupportsExternalTables,
        "DDL_ALL_EXTERNALS" => capabilities.SupportsExternalTables,
        "DDL_TABLE_CLIP" or "DDL_VIEW_CLIP" => capabilities.SupportsDdlToClipboard,
        "SELECT" or "SELECT_VIEW" => capabilities.SupportsTop100Select,
        "COUNT_ROWS" => capabilities.SupportsRowCount,
        "DUPLICATES_CLIP" => capabilities.SupportsDuplicateSearch,
        "DELETED" => capabilities.SupportsDeletedRows,
        "GRANT_CLIP" => capabilities.SupportsGrants,
        "GROOM" => capabilities.SupportsGroomTable,
        "DISTRIBUTE_CHART_NZ" or "DISTRIBUTE_CLIP" => capabilities.SupportsTableDistribution,
        "STATS" => capabilities.SupportsTableStatistics,
        "EMPTY" => capabilities.SupportsTruncateTable,
        "RECREATE_TABLE" => capabilities.SupportsRecreateTable,
        "RECREATE_ALL_TABLES" => capabilities.SupportsRecreateAllTables,
        "IMPORT_DATA" => capabilities.SupportsImport,
        "EXPORT_DATA" => capabilities.SupportsExport,
        "EXPORT_DDL_DATA" => capabilities.SupportsExport && capabilities.SupportsDdl,
        "COMMENT" => capabilities.SupportsComments,
        "KEY" or "UNIQUE" => capabilities.SupportsConstraints,
        "RENAME" => capabilities.SupportsRenameTable,
        "CREATE_FROM" => capabilities.SupportsCreateTableFrom,
        "EXPLAIN" => capabilities.SupportsExplain,
        "COMPARE_DDL" => capabilities.SupportsDdl,
        "FIND_USAGES" => capabilities.SupportsFindUsages,
        "IMPACT_ANALYSIS" => capabilities.SupportsFindUsages,
        "COPY_TO_CONNECTION" => capabilities.SupportsExport,
        "DROP" => capabilities.SupportsDropObject,
        "CREATE_INDEX" => capabilities.SupportsIndexes,
        "DDL_ALL_INDEXES" or "DDL_INDEX" or "DDL_INDEX_CLIP" or "DROP_INDEX" => capabilities.SupportsIndexes && capabilities.SupportsIndexDdl,
        "CREATE_PARTITION" or "DDL_ALL_PARTITIONS" or "DDL_PARTITION" or "DDL_PARTITION_CLIP" => capabilities.SupportsPartitions,
        "DDL_ALL_TRIGGERS" or "DDL_TRIGGER" or "DDL_TRIGGER_CLIP" or "DROP_TRIGGER" => capabilities.SupportsTriggers && capabilities.SupportsTriggerDdl,
        "CREATE_SYNONYM" => capabilities.SupportsSynonyms && capabilities.SupportsCreateTemplates,
        "DDL_SYNONYM" or "DDL_ALL_SYNONYMS" => capabilities.SupportsSynonyms,
        "CREATE_SEQUENCE" => capabilities.SupportsSequences && capabilities.SupportsCreateTemplates,
        "CREATE_PROCEDURE" => capabilities.SupportsProcedures && capabilities.SupportsCreateTemplates,
        "CALL_PROCEDURE" => capabilities.SupportsProcedures,
        "FLUID_SAMPLE" => capabilities.SupportsFluids,
        "SELECT_ALL_SEARCH_TEXT" or "SELECT_ALL_SEARCH_NUMBER" => capabilities.SupportsSchemaValueSearch,
        _ => true,
    };

    /// <summary>
    /// Engine-specific label for actions whose SQL meaning changes per database
    /// (Db2 uses REORG/RUNSTATS, PostgreSQL VACUUM/ANALYZE, SQLite ANALYZE where
    /// Netezza uses GROOM/GENERATE STATISTICS).
    /// </summary>
    public static string ResolveTitle(string? commandParameter, string defaultTitle, DatabaseTypeEnum databaseType)
        => (databaseType, commandParameter) switch
        {
            (DatabaseTypeEnum.DB2, "GROOM") => "Reorganize table (REORG)…",
            (DatabaseTypeEnum.DB2, "STATS") => "Run statistics (RUNSTATS)…",
            (DatabaseTypeEnum.DB2, "CREATE_SYNONYM") => "Create alias template",
            (DatabaseTypeEnum.DB2, "DDL_SYNONYM") => "Create alias/nickname DDL to new query window",
            (DatabaseTypeEnum.DB2, "DDL_ALL_SYNONYMS") => "Create all alias/nickname DDL to new query window",
            (DatabaseTypeEnum.PostgreSql, "GROOM") => "Vacuum table (VACUUM)…",
            (DatabaseTypeEnum.PostgreSql, "STATS") => "Analyze table (ANALYZE)…",
            (DatabaseTypeEnum.Sqlite, "GROOM") => "Vacuum database (VACUUM)…",
            (DatabaseTypeEnum.Sqlite, "STATS") => "Update statistics (ANALYZE)…",
            _ => defaultTitle,
        };

    /// <summary>Formats shared SQL when <paramref name="kind"/> maps to a Core template.</summary>
    public static string? TryFormatSharedSql(SchemaContextActionKind kind, string qualifiedObject)
    {
        string? id = Entries.FirstOrDefault(e => e.Kind == kind)?.SharedActionId;
        if (id is null)
            return null;

        var action = global::JustyBase.Core.Schema.SchemaContextMenuCatalog.Default
            .FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
        if (action is null)
            return null;
        // Host-only placeholders start with "-- Host:" (works without IsHostOnly on older Core).
        if (action.SqlTemplate.StartsWith("-- Host:", StringComparison.Ordinal))
            return null;
        return global::JustyBase.Core.Schema.SchemaContextMenuCatalog.Format(action, qualifiedObject);
    }

    /// <summary>
    /// Maps a catalog action to the existing <c>ContextMenuActionCommand</c> parameter string.
    /// Returns null when the action is not applicable for the given object type.
    /// </summary>
    public static string? GetCommandParameter(SchemaContextActionKind kind, TypeInDatabaseEnum type)
    {
        return kind switch
        {
            SchemaContextActionKind.DdlToTab => type switch
            {
                TypeInDatabaseEnum.View => "DDL_VIEW",
                TypeInDatabaseEnum.ExternalTable => "DDL_EXTERNAL",
                _ => "DDL_TABLE"
            },
            SchemaContextActionKind.DdlToClipboard => type switch
            {
                TypeInDatabaseEnum.View => "DDL_VIEW_CLIP",
                _ => "DDL_TABLE_CLIP"
            },
            SchemaContextActionKind.Top100 => type == TypeInDatabaseEnum.View ? "SELECT_VIEW" : "SELECT",
            SchemaContextActionKind.CountRows => "COUNT_ROWS",
            SchemaContextActionKind.Duplicates => "DUPLICATES_CLIP",
            SchemaContextActionKind.DeletedRows => "DELETED",
            SchemaContextActionKind.GrantTemplate => "GRANT_CLIP",
            SchemaContextActionKind.Groom => "GROOM",
            SchemaContextActionKind.DistributionShow => "DISTRIBUTE_CHART_NZ",
            SchemaContextActionKind.DistributionChange => "DISTRIBUTE_CLIP",
            SchemaContextActionKind.GenerateStatistics => "STATS",
            SchemaContextActionKind.EmptyTable => "EMPTY",
            SchemaContextActionKind.Recreate => "RECREATE_TABLE",
            SchemaContextActionKind.ImportData => "IMPORT_DATA",
            SchemaContextActionKind.ExportData => "EXPORT_DATA",
            SchemaContextActionKind.ExportDdlAndData => "EXPORT_DDL_DATA",
            SchemaContextActionKind.Comment => "COMMENT",
            SchemaContextActionKind.Drop => type switch
            {
                TypeInDatabaseEnum.Index => "DROP_INDEX",
                TypeInDatabaseEnum.Trigger => "DROP_TRIGGER",
                _ => "DROP",
            },
            SchemaContextActionKind.PrimaryKey => "KEY",
            SchemaContextActionKind.Unique => "UNIQUE",
            SchemaContextActionKind.Rename => "RENAME",
            SchemaContextActionKind.Explain => "EXPLAIN",
            SchemaContextActionKind.Compare => "COMPARE_DDL",
            SchemaContextActionKind.FindUsages => "FIND_USAGES",
            SchemaContextActionKind.ImpactAnalysis => "IMPACT_ANALYSIS",
            SchemaContextActionKind.CopyToConnection => "COPY_TO_CONNECTION",
            SchemaContextActionKind.CreateTableFrom => "CREATE_FROM",
            _ => null
        };
    }
}
