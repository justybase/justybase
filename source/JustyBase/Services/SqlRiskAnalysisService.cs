using JustyBase.NetezzaSqlParser.Linter;

namespace JustyBase.Services;

/// <summary>
/// Host adapter that maps <see cref="JustyBase.Core.Risk.SqlRiskAnalysisService"/>
/// into editor <see cref="LintIssue"/> diagnostics.
/// </summary>
public static class SqlRiskAnalysisService
{
    private static readonly JustyBase.Core.Risk.SqlRiskAnalysisService Shared = new();

    public static IReadOnlyList<LintIssue> Analyze(string sql, string? driverName = null)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return [];

        return Shared.Analyze(sql, driverName)
            .Select(ToLintIssue)
            .ToArray();
    }

    private static LintIssue ToLintIssue(JustyBase.Core.Risk.SqlRisk risk)
    {
        string code = risk.Kind switch
        {
            JustyBase.Core.Risk.SqlRiskKind.UnsafeUpdateDelete => "RISK001",
            JustyBase.Core.Risk.SqlRiskKind.MissingDistribute => "RISK002",
            JustyBase.Core.Risk.SqlRiskKind.SelectInto => "RISK003",
            _ => "RISK000"
        };

        return new LintIssue(
            code,
            risk.Message,
            LintSeverity.Warning,
            0,
            1);
    }
}
