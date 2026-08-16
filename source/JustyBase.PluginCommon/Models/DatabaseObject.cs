using JustyBase.PluginCommon.Enums;

namespace JustyBase.PluginCommon.Models;

public record DatabaseObject(int Id, string Name, string? Desc, TypeInDatabaseEnum TypeInDatabase, string TextType, string Owner, DateTime? CreateDateTime)
{
    /// <summary>
    /// SQLite uses sqlite_schema.tbl_name to associate indexes and triggers with
    /// their target table/view. This is intentionally separate from Desc, which
    /// remains user-facing description/source text for other database engines.
    /// </summary>
    public string? ParentObjectName { get; init; }

    /// <summary>Original catalog definition when the driver can provide it.</summary>
    public string? DefinitionSql { get; init; }

    /// <summary>True for internal/generated catalog rows hidden by default in UI.</summary>
    public bool IsSystemObject { get; init; }
}
