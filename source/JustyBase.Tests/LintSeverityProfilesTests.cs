using JustyBase.Common;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.Services;

namespace JustyBase.Tests;

public sealed class LintSeverityProfilesTests
{
    [Theory]
    [MemberData(nameof(DialectsWithRules))]
    public void DisabledLinterTurnsEveryRegisteredRuleOff(SqlDialect dialect)
    {
        QualityRuleRegistry registry = CreateRegistry(dialect);
        var options = new AppOptions { SqlLinterEnabled = false };

        LintSeverityProfiles.Apply(registry, dialect, options);

        Assert.All(registry.AllRules,
            rule => Assert.Equal(RuleSeverityConfig.Off, registry.GetEffectiveSeverity(rule.Id)));
    }

    [Theory]
    [InlineData(SqlDialect.Netezza, "NZ001")]
    [InlineData(SqlDialect.Db2, "DB2L001")]
    [InlineData(SqlDialect.Oracle, "ORA001")]
    [InlineData(SqlDialect.Mssql, "MSS001")]
    [InlineData(SqlDialect.Access, "ACC001")]
    public void SelectStarOptionMapsToDialectRule(SqlDialect dialect, string expectedRuleId)
    {
        QualityRuleRegistry registry = CreateRegistry(dialect);
        var options = new AppOptions { LintSeverityNz001 = "Error" };

        LintSeverityProfiles.Apply(registry, dialect, options);

        Assert.Equal(RuleSeverityConfig.Error, registry.GetEffectiveSeverity(expectedRuleId));
    }

    [Theory]
    [InlineData(SqlDialect.Netezza, "NZ002")]
    [InlineData(SqlDialect.Db2, "DB2L002")]
    [InlineData(SqlDialect.Oracle, "ORA002")]
    [InlineData(SqlDialect.Mssql, "MSS002")]
    [InlineData(SqlDialect.Access, "ACC002")]
    public void DeleteWithoutWhereOptionMapsToDialectRule(SqlDialect dialect, string expectedRuleId)
    {
        QualityRuleRegistry registry = CreateRegistry(dialect);
        var options = new AppOptions { LintSeverityNz002 = "Off" };

        LintSeverityProfiles.Apply(registry, dialect, options);

        Assert.Equal(RuleSeverityConfig.Off, registry.GetEffectiveSeverity(expectedRuleId));
    }

    [Theory]
    [InlineData(SqlDialect.Netezza, "NZ003")]
    [InlineData(SqlDialect.Db2, "DB2L003")]
    [InlineData(SqlDialect.Oracle, "ORA003")]
    [InlineData(SqlDialect.Mssql, "MSS003")]
    [InlineData(SqlDialect.Access, "ACC003")]
    public void UpdateWithoutWhereOptionMapsToDialectRule(SqlDialect dialect, string expectedRuleId)
    {
        QualityRuleRegistry registry = CreateRegistry(dialect);
        var options = new AppOptions { LintSeverityNz003 = "Information" };

        LintSeverityProfiles.Apply(registry, dialect, options);

        Assert.Equal(RuleSeverityConfig.Information, registry.GetEffectiveSeverity(expectedRuleId));
    }

    [Fact]
    public void NetezzaKeepsLegacyAliasesAndExtraRules()
    {
        var registry = new QualityRuleRegistry();
        var options = new AppOptions
        {
            LintSeverityNz002 = "Error",
            LintSeverityNz003 = "Hint",
            LintSeverityNz004 = "Off",
            LintSeverityNz102 = "Error",
        };

        LintSeverityProfiles.Apply(registry, SqlDialect.Netezza, options);

        Assert.Equal(RuleSeverityConfig.Error, registry.GetEffectiveSeverity("NZ002"));
        Assert.Equal(RuleSeverityConfig.Error, registry.GetEffectiveSeverity("SQL043"));
        Assert.Equal(RuleSeverityConfig.Hint, registry.GetEffectiveSeverity("NZ003"));
        Assert.Equal(RuleSeverityConfig.Hint, registry.GetEffectiveSeverity("SQL044"));
        Assert.Equal(RuleSeverityConfig.Off, registry.GetEffectiveSeverity("NZ004"));
        Assert.Equal(RuleSeverityConfig.Error, registry.GetEffectiveSeverity("NZ102"));
    }

    [Fact]
    public void ReenablingAfterGlobalOffRestoresDefaultsAndOptions()
    {
        var registry = new QualityRuleRegistry(OracleLintRules.AllRules);
        var options = new AppOptions { SqlLinterEnabled = false };

        LintSeverityProfiles.Apply(registry, SqlDialect.Oracle, options);
        Assert.All(registry.AllRules,
            rule => Assert.Equal(RuleSeverityConfig.Off, registry.GetEffectiveSeverity(rule.Id)));

        options = new AppOptions { SqlLinterEnabled = true, LintSeverityNz001 = "Error" };
        LintSeverityProfiles.Apply(registry, SqlDialect.Oracle, options);

        Assert.Equal(RuleSeverityConfig.Error, registry.GetEffectiveSeverity("ORA001"));
        Assert.Equal(RuleSeverityConfig.Warning, registry.GetEffectiveSeverity("ORA002"));
    }

    [Theory]
    [InlineData(SqlDialect.PostgreSql)]
    [InlineData(SqlDialect.MySql)]
    [InlineData(SqlDialect.Sqlite)]
    public void DialectsWithoutCoreRulesDoNotThrow(SqlDialect dialect)
    {
        QualityRuleRegistry registry = CreateRegistry(dialect);
        var options = new AppOptions { LintSeverityNz001 = "Error" };

        LintSeverityProfiles.Apply(registry, dialect, options);

        Assert.NotNull(registry.AllRules);
    }

    public static IEnumerable<object[]> DialectsWithRules() =>
    [
        [SqlDialect.Netezza],
        [SqlDialect.Db2],
        [SqlDialect.Oracle],
        [SqlDialect.Mssql],
        [SqlDialect.Access],
        [SqlDialect.Sqlite],
    ];

    private static QualityRuleRegistry CreateRegistry(SqlDialect dialect) => dialect switch
    {
        SqlDialect.Netezza => new QualityRuleRegistry(),
        SqlDialect.Db2 => new QualityRuleRegistry(Db2LintRules.AllRules),
        SqlDialect.Oracle => new QualityRuleRegistry(OracleLintRules.AllRules),
        SqlDialect.Mssql => new QualityRuleRegistry(MssqlLintRules.AllRules),
        SqlDialect.Access => new QualityRuleRegistry(AccessLintRules.AllRules),
        SqlDialect.Sqlite => new QualityRuleRegistry(SqliteLintRules.AllRules),
        _ => new QualityRuleRegistry(PostgreSqlLintRules.AllRules),
    };
}
