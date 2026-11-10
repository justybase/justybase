using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.Services.Schema;

namespace JustyBase.Tests;

public sealed class SchemaTreeGroupsTests
{
    [Fact]
    public void Netezza_KeepsItsCatalogGroupsWithoutIndexesOrTriggers()
    {
        var types = Build(DatabaseTypeEnum.NetezzaSQL);

        Assert.Contains(TypeInDatabaseEnum.baseTables, types);
        Assert.Contains(TypeInDatabaseEnum.baseExternals, types);
        Assert.Contains(TypeInDatabaseEnum.baseViews, types);
        Assert.Contains(TypeInDatabaseEnum.baseProcedures, types);
        Assert.Contains(TypeInDatabaseEnum.baseSequence, types);
        Assert.Contains(TypeInDatabaseEnum.baseFunctions, types);
        Assert.Contains(TypeInDatabaseEnum.baseSynonyms, types);
        Assert.Contains(TypeInDatabaseEnum.baseAggregates, types);
        Assert.Contains(TypeInDatabaseEnum.baseFluides, types);
        Assert.Contains(TypeInDatabaseEnum.otherNoneGroup, types);

        Assert.DoesNotContain(TypeInDatabaseEnum.baseIndexes, types);
        Assert.DoesNotContain(TypeInDatabaseEnum.basePartitions, types);
        Assert.DoesNotContain(TypeInDatabaseEnum.baseTriggers, types);
    }

    [Fact]
    public void Db2_KeepsTriggersEvenWhenIndexesAndPartitionsArePresent()
    {
        var types = Build(DatabaseTypeEnum.DB2);

        Assert.Contains(TypeInDatabaseEnum.baseIndexes, types);
        Assert.Contains(TypeInDatabaseEnum.basePartitions, types);
        Assert.Contains(TypeInDatabaseEnum.baseTriggers, types);
        Assert.Contains(TypeInDatabaseEnum.baseSynonyms, types);
        Assert.Contains(TypeInDatabaseEnum.baseAggregates, types);

        Assert.DoesNotContain(TypeInDatabaseEnum.baseExternals, types);
        Assert.DoesNotContain(TypeInDatabaseEnum.baseFluides, types);
    }

    [Fact]
    public void PostgreSql_HasIndexesPartitionsSequencesAndFunctions()
    {
        var types = Build(DatabaseTypeEnum.PostgreSql);

        Assert.Contains(TypeInDatabaseEnum.baseIndexes, types);
        Assert.Contains(TypeInDatabaseEnum.basePartitions, types);
        Assert.Contains(TypeInDatabaseEnum.baseSequence, types);
        Assert.Contains(TypeInDatabaseEnum.baseFunctions, types);
        Assert.Contains(TypeInDatabaseEnum.baseProcedures, types);

        Assert.DoesNotContain(TypeInDatabaseEnum.baseTriggers, types);
        Assert.DoesNotContain(TypeInDatabaseEnum.baseSynonyms, types);
        Assert.DoesNotContain(TypeInDatabaseEnum.baseAggregates, types);
        Assert.DoesNotContain(TypeInDatabaseEnum.baseExternals, types);
    }

    [Fact]
    public void Oracle_BrowsesIndexesTriggersAndSynonyms()
    {
        var types = Build(DatabaseTypeEnum.Oracle);

        Assert.Contains(TypeInDatabaseEnum.baseIndexes, types);
        Assert.Contains(TypeInDatabaseEnum.baseTriggers, types);
        Assert.Contains(TypeInDatabaseEnum.baseSynonyms, types);
        Assert.Contains(TypeInDatabaseEnum.baseSequence, types);
        Assert.Contains(TypeInDatabaseEnum.baseFunctions, types);
        Assert.Contains(TypeInDatabaseEnum.baseProcedures, types);

        Assert.DoesNotContain(TypeInDatabaseEnum.baseAggregates, types);
        Assert.DoesNotContain(TypeInDatabaseEnum.basePartitions, types);
        Assert.DoesNotContain(TypeInDatabaseEnum.baseExternals, types);
    }

    [Fact]
    public void Sqlite_HasIndexesAndTriggersOnly()
    {
        var types = Build(DatabaseTypeEnum.Sqlite);

        Assert.Contains(TypeInDatabaseEnum.baseIndexes, types);
        Assert.Contains(TypeInDatabaseEnum.baseTriggers, types);

        Assert.DoesNotContain(TypeInDatabaseEnum.baseSequence, types);
        Assert.DoesNotContain(TypeInDatabaseEnum.baseFunctions, types);
        Assert.DoesNotContain(TypeInDatabaseEnum.baseProcedures, types);
        Assert.DoesNotContain(TypeInDatabaseEnum.baseSynonyms, types);
        Assert.DoesNotContain(TypeInDatabaseEnum.baseAggregates, types);
    }

    [Theory]
    [InlineData(DatabaseTypeEnum.MySql)]
    [InlineData(DatabaseTypeEnum.DuckDB)]
    [InlineData(DatabaseTypeEnum.Access)]
    [InlineData(DatabaseTypeEnum.Excel)]
    public void StubAndFileEngines_OnlyShowTablesViewsAndOthers(DatabaseTypeEnum databaseType)
    {
        var types = Build(databaseType);
        TypeInDatabaseEnum[] expected =
        [
            TypeInDatabaseEnum.baseTables,
            TypeInDatabaseEnum.baseViews,
            TypeInDatabaseEnum.otherNoneGroup,
        ];

        Assert.Equal(expected, types);
    }

    [Fact]
    public void Build_AlwaysEndsWithOthersAndHasUniqueTypes()
    {
        foreach (DatabaseTypeEnum databaseType in Enum.GetValues<DatabaseTypeEnum>())
        {
            var groups = SchemaTreeGroups.Build(DatabaseCapabilitiesCatalog.For(databaseType));

            Assert.Equal(TypeInDatabaseEnum.otherNoneGroup, groups[^1].Type);
            Assert.Equal(groups.Count, groups.Select(g => g.Type).Distinct().Count());
        }
    }

    private static TypeInDatabaseEnum[] Build(DatabaseTypeEnum databaseType)
        => SchemaTreeGroups.Build(DatabaseCapabilitiesCatalog.For(databaseType))
            .Select(group => group.Type)
            .ToArray();
}
