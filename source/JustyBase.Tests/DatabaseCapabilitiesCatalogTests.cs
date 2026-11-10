using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;

namespace JustyBase.Tests;

public sealed class DatabaseCapabilitiesCatalogTests
{
    [Fact]
    public void EveryDatabaseTypeHasAPreset()
    {
        foreach (DatabaseTypeEnum type in Enum.GetValues<DatabaseTypeEnum>())
        {
            Assert.True(DatabaseCapabilitiesCatalog.ByDatabaseType.ContainsKey(type),
                $"{type} has no capability preset and would silently use Generic.");
        }
    }

    [Fact]
    public void ShippedEnginesDoNotUseTheGenericFallback()
    {
        DatabaseCapabilities generic = DatabaseCapabilitiesCatalog.For(DatabaseTypeEnum.NotSupportedDatabase);

        foreach (DatabaseTypeEnum type in new[]
                 {
                     DatabaseTypeEnum.NetezzaSQL,
                     DatabaseTypeEnum.DB2,
                     DatabaseTypeEnum.PostgreSql,
                     DatabaseTypeEnum.Oracle,
                     DatabaseTypeEnum.DuckDB,
                     DatabaseTypeEnum.MySql,
                     DatabaseTypeEnum.Sqlite,
                     DatabaseTypeEnum.Access,
                     DatabaseTypeEnum.Excel,
                 })
        {
            Assert.NotSame(generic, DatabaseCapabilitiesCatalog.For(type));
        }
    }

    [Fact]
    public void PostgreSql_HidesUnimplementedCatalogSurfaces()
    {
        DatabaseCapabilities capabilities = DatabaseCapabilitiesCatalog.For(DatabaseTypeEnum.PostgreSql);

        Assert.True(capabilities.SupportsGroomTable);
        Assert.True(capabilities.SupportsTableStatistics);
        Assert.True(capabilities.SupportsComments);
        Assert.True(capabilities.SupportsIndexes);
        Assert.True(capabilities.SupportsIndexDdl);
        Assert.True(capabilities.SupportsPartitions);
        Assert.True(capabilities.SupportsSequences);
        Assert.True(capabilities.SupportsFunctions);
        Assert.True(capabilities.SupportsProcedures);
        Assert.True(capabilities.SupportsDropObject);
        Assert.True(capabilities.SupportsConstraints);

        Assert.False(capabilities.SupportsSynonyms);
        Assert.False(capabilities.SupportsAggregates);
        Assert.False(capabilities.SupportsTriggers);
        Assert.False(capabilities.SupportsExternalTables);
        Assert.False(capabilities.SupportsTablespaces);
        Assert.False(capabilities.SupportsCreateTableFrom);
        Assert.False(capabilities.SupportsDeletedRows);
        Assert.False(capabilities.SupportsGrants);
        Assert.False(capabilities.SupportsTableDistribution);
    }

    [Fact]
    public void Oracle_BrowsesIndexesAndTriggersButHidesTheirDdl()
    {
        DatabaseCapabilities capabilities = DatabaseCapabilitiesCatalog.For(DatabaseTypeEnum.Oracle);

        Assert.True(capabilities.SupportsIndexes);
        Assert.False(capabilities.SupportsIndexDdl);
        Assert.True(capabilities.SupportsTriggers);
        Assert.False(capabilities.SupportsTriggerDdl);
        Assert.True(capabilities.SupportsSynonyms);
        Assert.True(capabilities.SupportsSequences);
        Assert.True(capabilities.SupportsComments);
        Assert.True(capabilities.SupportsExplain);
        Assert.True(capabilities.SupportsDropObject);
        Assert.True(capabilities.SupportsConstraints);

        Assert.False(capabilities.SupportsGroomTable);
        Assert.False(capabilities.SupportsTableStatistics);
        Assert.False(capabilities.SupportsAggregates);
        Assert.False(capabilities.SupportsPartitions);
        Assert.False(capabilities.SupportsTablespaces);
        Assert.False(capabilities.SupportsExternalTables);
        Assert.False(capabilities.SupportsCreateTableFrom);
    }

    [Fact]
    public void MySql_StubExposesOnlyImplementedActions()
    {
        DatabaseCapabilities capabilities = DatabaseCapabilitiesCatalog.For(DatabaseTypeEnum.MySql);

        Assert.True(capabilities.SupportsExplain);
        Assert.True(capabilities.SupportsTop100Select);
        Assert.True(capabilities.SupportsRowCount);
        Assert.True(capabilities.SupportsDuplicateSearch);
        Assert.True(capabilities.SupportsTruncateTable);
        Assert.True(capabilities.SupportsImport);
        Assert.True(capabilities.SupportsExport);
        Assert.True(capabilities.SupportsRenameTable);
        Assert.True(capabilities.SupportsDropObject);
        Assert.True(capabilities.SupportsConstraints);

        Assert.False(capabilities.SupportsDdl);
        Assert.False(capabilities.SupportsDdlToClipboard);
        Assert.False(capabilities.SupportsComments);
        Assert.False(capabilities.SupportsGroomTable);
        Assert.False(capabilities.SupportsTableStatistics);
        Assert.False(capabilities.SupportsSynonyms);
        Assert.False(capabilities.SupportsSequences);
        Assert.False(capabilities.SupportsFunctions);
        Assert.False(capabilities.SupportsProcedures);
        Assert.False(capabilities.SupportsAggregates);
        Assert.False(capabilities.SupportsIndexes);
        Assert.False(capabilities.SupportsTriggers);
        Assert.False(capabilities.SupportsExternalTables);
        Assert.False(capabilities.SupportsCreateTemplates);
        Assert.False(capabilities.SupportsCreateTableFrom);
    }

    [Fact]
    public void DuckDb_HidesProceduresSynonymsAndStatistics()
    {
        DatabaseCapabilities capabilities = DatabaseCapabilitiesCatalog.For(DatabaseTypeEnum.DuckDB);

        Assert.True(capabilities.SupportsDdl);
        Assert.True(capabilities.SupportsExplain);
        Assert.True(capabilities.SupportsImport);
        Assert.True(capabilities.SupportsExport);
        Assert.True(capabilities.SupportsTruncateTable);
        Assert.True(capabilities.SupportsDropObject);

        Assert.False(capabilities.SupportsComments);
        Assert.False(capabilities.SupportsGroomTable);
        Assert.False(capabilities.SupportsTableStatistics);
        Assert.False(capabilities.SupportsSynonyms);
        Assert.False(capabilities.SupportsSequences);
        Assert.False(capabilities.SupportsFunctions);
        Assert.False(capabilities.SupportsProcedures);
        Assert.False(capabilities.SupportsAggregates);
        Assert.False(capabilities.SupportsIndexes);
        Assert.False(capabilities.SupportsTriggers);
        Assert.False(capabilities.SupportsExternalTables);
        Assert.False(capabilities.SupportsConstraints);
    }

    [Fact]
    public void Sqlite_HidesProcedureSynonymSequenceAndAggregateGroups()
    {
        DatabaseCapabilities capabilities = DatabaseCapabilitiesCatalog.For(DatabaseTypeEnum.Sqlite);

        Assert.True(capabilities.SupportsGroomTable);
        Assert.True(capabilities.SupportsTableStatistics);
        Assert.True(capabilities.SupportsIndexes);
        Assert.True(capabilities.SupportsIndexDdl);
        Assert.True(capabilities.SupportsTriggers);
        Assert.True(capabilities.SupportsTriggerDdl);
        Assert.True(capabilities.SupportsCreateTableFrom);
        Assert.True(capabilities.SupportsDropObject);

        Assert.False(capabilities.SupportsComments);
        Assert.False(capabilities.SupportsSynonyms);
        Assert.False(capabilities.SupportsSequences);
        Assert.False(capabilities.SupportsFunctions);
        Assert.False(capabilities.SupportsProcedures);
        Assert.False(capabilities.SupportsAggregates);
        Assert.False(capabilities.SupportsConstraints);
    }

    [Fact]
    public void Access_HidesUnsupportedCatalogAndDestructiveActions()
    {
        DatabaseCapabilities capabilities = DatabaseCapabilitiesCatalog.For(DatabaseTypeEnum.Access);

        Assert.True(capabilities.SupportsDdl);
        Assert.True(capabilities.SupportsDdlToClipboard);
        Assert.True(capabilities.SupportsTop100Select);
        Assert.True(capabilities.SupportsTruncateTable);
        Assert.True(capabilities.SupportsImport);
        Assert.True(capabilities.SupportsExport);
        Assert.True(capabilities.SupportsCreateTableFrom);
        Assert.True(capabilities.SupportsDropObject);
        Assert.True(capabilities.SupportsConstraints);

        Assert.False(capabilities.SupportsComments);
        Assert.False(capabilities.SupportsExplain);
        Assert.False(capabilities.SupportsDuplicateSearch);
        Assert.False(capabilities.SupportsGroomTable);
        Assert.False(capabilities.SupportsTableStatistics);
        Assert.False(capabilities.SupportsSynonyms);
        Assert.False(capabilities.SupportsSequences);
        Assert.False(capabilities.SupportsFunctions);
        Assert.False(capabilities.SupportsProcedures);
        Assert.False(capabilities.SupportsAggregates);
        Assert.False(capabilities.SupportsIndexes);
        Assert.False(capabilities.SupportsTriggers);
        Assert.False(capabilities.SupportsSchemaValueSearch);
    }

    [Fact]
    public void Excel_IsReadOnly()
    {
        DatabaseCapabilities capabilities = DatabaseCapabilitiesCatalog.For(DatabaseTypeEnum.Excel);

        Assert.True(capabilities.SupportsDdl);
        Assert.True(capabilities.SupportsDdlToClipboard);
        Assert.True(capabilities.SupportsExplain);
        Assert.True(capabilities.SupportsTop100Select);
        Assert.True(capabilities.SupportsExport);

        Assert.False(capabilities.SupportsDropObject);
        Assert.False(capabilities.SupportsConstraints);
        Assert.False(capabilities.SupportsTruncateTable);
        Assert.False(capabilities.SupportsRenameTable);
        Assert.False(capabilities.SupportsImport);
        Assert.False(capabilities.SupportsComments);
        Assert.False(capabilities.SupportsTableStatistics);
        Assert.False(capabilities.SupportsSynonyms);
        Assert.False(capabilities.SupportsSequences);
        Assert.False(capabilities.SupportsFunctions);
        Assert.False(capabilities.SupportsProcedures);
        Assert.False(capabilities.SupportsAggregates);
        Assert.False(capabilities.SupportsIndexes);
        Assert.False(capabilities.SupportsTriggers);
        Assert.False(capabilities.SupportsCreateTemplates);
    }
}
