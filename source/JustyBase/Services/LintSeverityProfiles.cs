using JustyBase.Common;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Linter;

namespace JustyBase.Services;

/// <summary>
/// Applies the shared linter options (<see cref="AppOptions"/>) to the quality
/// rule registry of the active SQL dialect.
/// </summary>
/// <remarks>
/// The three core rules are matched by <see cref="LintRule.Name"/> so every
/// dialect family (NZ, DB2L, ORA, MSS, ACC) follows the same Select Star /
/// Delete without WHERE / Update without WHERE options without a hard-coded id
/// table. Netezza keeps its historical extra mappings (NZ004, NZ005, …).
/// The global switch disables every rule registered for the active dialect,
/// which also covers procedure and expensive rules that the old hard-coded
/// list missed.
/// </remarks>
public static class LintSeverityProfiles
{
    private const string SelectStarRuleName = "Select Star";
    private const string DeleteWithoutWhereRuleName = "Delete Without Where";
    private const string UpdateWithoutWhereRuleName = "Update Without Where";

    /// <summary>
    /// Netezza-only options that have no cross-dialect equivalent. SQL043/SQL044
    /// are legacy aliases of NZ002/NZ003 and must follow the same options.
    /// </summary>
    private static readonly (string RuleId, Func<AppOptions, string> Severity)[] NetezzaExtras =
    [
        ("NZ004", static o => o.LintSeverityNz004),
        ("NZ005", static o => o.LintSeverityNz005),
        ("NZ008", static o => o.LintSeverityNz008),
        ("NZ011", static o => o.LintSeverityNz011),
        ("SQL045", static o => o.LintSeverityNz011),
        ("NZ012", static o => o.LintSeverityNz012),
        ("SQL046", static o => o.LintSeverityNz012),
        ("NZ013", static o => o.LintSeverityNz013),
        ("NZ015", static o => o.LintSeverityNz015),
        ("NZ025", static o => o.LintSeverityNz025),
        ("NZ102", static o => o.LintSeverityNz102),
        ("SQL043", static o => o.LintSeverityNz002),
        ("SQL044", static o => o.LintSeverityNz003),
    ];

    public static void Apply(QualityRuleRegistry registry, SqlDialect dialect, AppOptions options)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);

        // Restores rule defaults and re-applies parser-owned superseded ids.
        registry.ResetSeverities();

        if (!options.SqlLinterEnabled)
        {
            foreach (LintRule rule in registry.AllRules)
            {
                registry.SetSeverity(rule.Id, RuleSeverityConfig.Off);
            }

            return;
        }

        ApplyByName(registry, SelectStarRuleName, options.LintSeverityNz001);
        ApplyByName(registry, DeleteWithoutWhereRuleName, options.LintSeverityNz002);
        ApplyByName(registry, UpdateWithoutWhereRuleName, options.LintSeverityNz003);

        if (dialect != SqlDialect.Netezza)
        {
            return;
        }

        foreach ((string ruleId, Func<AppOptions, string> severity) in NetezzaExtras)
        {
            registry.SetSeverity(ruleId, ToConfig(severity(options)));
        }
    }

    private static void ApplyByName(QualityRuleRegistry registry, string ruleName, string severity)
    {
        RuleSeverityConfig config = ToConfig(severity);
        foreach (LintRule rule in registry.AllRules)
        {
            if (string.Equals(rule.Name, ruleName, StringComparison.OrdinalIgnoreCase))
            {
                registry.SetSeverity(rule.Id, config);
            }
        }
    }

    private static RuleSeverityConfig ToConfig(string? severity) => severity?.Trim().ToUpperInvariant() switch
    {
        "OFF" => RuleSeverityConfig.Off,
        "ERROR" => RuleSeverityConfig.Error,
        "INFORMATION" or "INFO" => RuleSeverityConfig.Information,
        "HINT" => RuleSeverityConfig.Hint,
        _ => RuleSeverityConfig.Warning,
    };
}
