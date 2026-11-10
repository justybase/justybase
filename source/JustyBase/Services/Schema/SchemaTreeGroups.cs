using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;

namespace JustyBase.Services.Schema;

/// <summary>One schema-level folder in the database tree.</summary>
public readonly record struct SchemaTreeGroup(string Title, string Info, TypeInDatabaseEnum Type);

/// <summary>
/// Builds the schema-level folder list for an engine. Each group is gated by an
/// independent capability flag so engines with, for example, both indexes and
/// partitions still get the triggers folder.
/// </summary>
public static class SchemaTreeGroups
{
    public static IReadOnlyList<SchemaTreeGroup> Build(DatabaseCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var groups = new List<SchemaTreeGroup>(13)
        {
            new("Tables", "tables", TypeInDatabaseEnum.baseTables),
        };

        if (capabilities.SupportsExternalTables)
        {
            groups.Add(new("External tables", "external tables", TypeInDatabaseEnum.baseExternals));
        }

        groups.Add(new("Views", "views", TypeInDatabaseEnum.baseViews));

        if (capabilities.SupportsProcedures)
        {
            groups.Add(new("Procedures", "procedures", TypeInDatabaseEnum.baseProcedures));
        }

        if (capabilities.SupportsSequences)
        {
            groups.Add(new("Sequences", "sequences", TypeInDatabaseEnum.baseSequence));
        }

        if (capabilities.SupportsFunctions)
        {
            groups.Add(new("Functions", "functions", TypeInDatabaseEnum.baseFunctions));
        }

        if (capabilities.SupportsSynonyms)
        {
            groups.Add(new("Synonyms", "synonyms", TypeInDatabaseEnum.baseSynonyms));
        }

        if (capabilities.SupportsAggregates)
        {
            groups.Add(new("Aggregate", "aggregate", TypeInDatabaseEnum.baseAggregates));
        }

        if (capabilities.SupportsIndexes)
        {
            groups.Add(new("Indexes", "indexes", TypeInDatabaseEnum.baseIndexes));
        }

        if (capabilities.SupportsPartitions)
        {
            groups.Add(new("Partitions", "partitions", TypeInDatabaseEnum.basePartitions));
        }

        if (capabilities.SupportsTriggers)
        {
            groups.Add(new("Triggers", "triggers", TypeInDatabaseEnum.baseTriggers));
        }

        if (capabilities.SupportsFluids)
        {
            groups.Add(new("Fluid Query Data Sources", "fluids", TypeInDatabaseEnum.baseFluides));
        }

        groups.Add(new("Others", "others", TypeInDatabaseEnum.otherNoneGroup));

        return groups;
    }
}
