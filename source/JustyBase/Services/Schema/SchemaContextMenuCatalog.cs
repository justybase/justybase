using JustyBase.PluginCommon.Enums;

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
    ExportData
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
    ];

    public static IEnumerable<SchemaContextMenuEntry> ForType(TypeInDatabaseEnum type)
        => Entries.Where(e => e.SupportedTypes.Contains(type)).OrderBy(e => e.SortOrder);

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
            _ => null
        };
    }
}
