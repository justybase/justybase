using Avalonia.Collections;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Linter;
using JustyBase.Common.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace JustyBase.ViewModels.Tools;

public sealed partial class SqlDiagnosticsViewModel : Tool
{
    public DataGridCollectionView DiagnosticsCollectionView { get; }

    private readonly AvaloniaList<DiagnosticItem> _items = new();

    public IReadOnlyList<DiagnosticItem> Items => _items;

    /// <summary>
    /// Optional delegate invoked when the user selects a diagnostic item.
    /// Receives the start offset and length of the relevant SQL span.
    /// </summary>
    public Action<int, int>? NavigateToOffset { get; set; }

    /// <summary>
    /// Optional delegate providing current SQL text for "Fix in AI Chat" command.
    /// </summary>
    public Func<string>? GetCurrentSql { get; set; }

    [ObservableProperty]
    public partial DiagnosticItem? SelectedItem { get; set; }

    partial void OnSelectedItemChanged(DiagnosticItem? value)
    {
        if (value is not null && NavigateToOffset is not null)
        {
            var length = value.EndOffset - value.StartOffset;
            if (length <= 0) length = 1;
            NavigateToOffset(value.StartOffset, length);
        }
    }

    [ObservableProperty]
    public partial string IssueCount { get; set; } = "0 issues";

    [ObservableProperty]
    public partial bool HasIssues { get; set; }

    /// <summary>
    /// Feature availability flag for AI Chat (reads the live master switch). When false, the
    /// "Fix in AI Chat" entry points in this panel are hidden.
    /// </summary>
    public bool IsAiChatEnabled
        => Program.ServiceProvider?.GetService<IGeneralApplicationData>()?.Config.EnableAiChat ?? false;

    // ── Performance metrics ────────────────────────────────────────────────

    [ObservableProperty]
    public partial string MetricsCacheHitRatio { get; set; } = "—";

    [ObservableProperty]
    public partial string MetricsCacheHitBar { get; set; } = "—";

    [ObservableProperty]
    public partial string MetricsCheapAvgMs { get; set; } = "—";

    [ObservableProperty]
    public partial string MetricsCheapCount { get; set; } = "—";

    [ObservableProperty]
    public partial string MetricsExpAvgMs { get; set; } = "—";

    [ObservableProperty]
    public partial string MetricsExpCount { get; set; } = "—";

    [ObservableProperty]
    public partial bool MetricsPanelVisible { get; set; }

    /// <summary>
    /// Update the metrics display from a LintEngineMetrics snapshot.
    /// Called from NzLinterService after each lint analysis pass.
    /// </summary>
    public void UpdateMetrics(LintMetricsSnapshot metrics)
    {
        var hasData = metrics.CheapRunCount > 0 || metrics.ExpensiveRunCount > 0;

        if (!hasData)
        {
            MetricsCacheHitRatio = "—";
            MetricsCacheHitBar = "—";
            MetricsCheapAvgMs = "—";
            MetricsCheapCount = "—";
            MetricsExpAvgMs = "—";
            MetricsExpCount = "—";
            return;
        }

        var ratioPct = metrics.CacheHitRatio * 100;
        MetricsCacheHitRatio = $"{ratioPct:F0}%";

        // Build a simple ASCII-ish bar: 10 blocks max
        var blocks = (int)(ratioPct / 10);
        var bar = new char[10];
        for (int i = 0; i < 10; i++)
            bar[i] = i < blocks ? '█' : '░';
        MetricsCacheHitBar = new string(bar);

        MetricsCheapCount = $"{metrics.CheapRunCount}";
        MetricsCheapAvgMs = $"{metrics.CheapAvgTimeMs:F1} ms";
        MetricsExpCount = $"{metrics.ExpensiveRunCount}";
        MetricsExpAvgMs = $"{metrics.ExpensiveAvgTimeMs:F1} ms";
    }

    public SqlDiagnosticsViewModel()
    {
        IssueCount = "0 issues";
        DiagnosticsCollectionView = new DataGridCollectionView(_items);
        DiagnosticsCollectionView.SortDescriptions.Add(
            DataGridSortDescription.FromPath("Severity", System.ComponentModel.ListSortDirection.Ascending));
    }

    /// <summary>
    /// Replaces the current diagnostics list.
    /// </summary>
    /// <param name="issues">Lint issues from the latest analysis pass.</param>
    /// <param name="getSql">
    /// Optional delegate that returns the live editor text.  When provided (together with
    /// <paramref name="setSql"/>) each item that has a known quick-fix will expose a
    /// <see cref="DiagnosticItem.QuickFixCommand"/> that can be invoked from the UI.
    /// </param>
    /// <param name="setSql">
    /// Optional delegate that writes corrected SQL back to the editor document.
    /// </param>
    public void UpdateDiagnostics(IReadOnlyList<LintIssue> issues,
        Func<string>? getSql = null, Action<string>? setSql = null)
    {
        _items.Clear();
        _getSql = getSql;
        _setSql = setSql;
        _lastIssues = issues;

        // Snapshot the SQL once for initial quick-fix context; the Apply lambdas always
        // call getSql() at execution time so they see the live text.
        var sqlSnapshot = getSql is not null ? getSql() : string.Empty;

        foreach (var issue in issues)
        {
            var quickFix = (getSql is not null && setSql is not null)
                ? NzLintCodeActions.GetQuickFix(issue, sqlSnapshot)
                : null;

            // Strip redundant rule ID prefix from message (e.g. "NZ001: ..." → "...")
            // when the message already starts with the rule code.
            var displayMessage = issue.Message;
            if (displayMessage.StartsWith(issue.RuleId + ": ", StringComparison.OrdinalIgnoreCase))
                displayMessage = displayMessage[(issue.RuleId.Length + 2)..];

            _items.Add(new DiagnosticItem(
                issue.RuleId,
                displayMessage,
                SeverityToString(issue.Severity),
                IssueSeverity(issue.Severity),
                issue.StartOffset,
                issue.EndOffset,
                issue.StartLine,
                issue.StartColumn,
                issue.EndLine,
                issue.EndColumn,
                getSql,
                setSql,
                quickFix));
        }

        IssueCount = _items.Count == 0 ? "No issues" : $"{_items.Count} issue{(_items.Count != 1 ? "s" : "")}";
        HasIssues = _items.Count > 0;
        HasSafeFixes = _lastIssues.Any(i => NzLintCodeActions.IsSafeForFixAll(i.RuleId));
    }

    private Func<string>? _getSql;
    private Action<string>? _setSql;
    private IReadOnlyList<LintIssue> _lastIssues = [];

    [ObservableProperty]
    public partial bool HasSafeFixes { get; set; }

    [RelayCommand]
    private void FixAllSafe()
    {
        if (_getSql is null || _setSql is null || _lastIssues.Count == 0) return;
        var current = _getSql();
        var fixedSql = NzLintCodeActions.ApplyAllSafeFixes(current, _lastIssues);
        if (fixedSql != current)
            _setSql(fixedSql);
    }

    private static string SeverityToString(LintSeverity s) => s switch
    {
        LintSeverity.Error => "Error",
        LintSeverity.Warning => "Warning",
        LintSeverity.Information => "Info",
        LintSeverity.Hint => "Hint",
        _ => "Unknown"
    };

    private static int IssueSeverity(LintSeverity s) => (int)s;

    [RelayCommand]
    private void Clear()
    {
        _items.Clear();
        _lastIssues = [];
        IssueCount = "0 issues";
        HasIssues = false;
        HasSafeFixes = false;
    }

    [RelayCommand]
    private async Task SendToAiChatAsync()
    {
        if (_items.Count == 0) return;

        var aiChatVm = Program.ServiceProvider?.GetService<AiChatViewModel>();
        if (aiChatVm is not null)
            await aiChatVm.SendToAiChatAsync();
    }

    [RelayCommand]
    private void ToggleMetricsPanel()
    {
        MetricsPanelVisible = !MetricsPanelVisible;
    }
}

/// <summary>
/// Represents a single diagnostic entry in the SQL diagnostics panel.
/// </summary>
public sealed class DiagnosticItem
{
    public string RuleId { get; }
    public string Message { get; }
    public string Severity { get; }
    public int SeverityOrder { get; }
    public int StartOffset { get; }
    public int EndOffset { get; }
    public int StartLine { get; }
    public int StartColumn { get; }
    public int EndLine { get; }
    public int EndColumn { get; }

    /// <summary>Unicode icon for the severity level.</summary>
    public string SeverityIcon => SeverityOrder switch
    {
        0 => "\u2716",   // ✖ Error
        1 => "\u26A0",   // ⚠ Warning
        2 => "\u2139",   // ℹ Info
        _ => "\u24D8",   // ⌘ Hint
    };

    /// <summary>Brush color for the severity level.</summary>
    public IBrush SeverityBrush => SeverityOrder switch
    {
        0 => new SolidColorBrush(Color.FromRgb(255, 68, 68)),    // Red
        1 => new SolidColorBrush(Color.FromRgb(255, 185, 0)),    // Amber
        2 => new SolidColorBrush(Color.FromRgb(86, 156, 214)),   // Blue
        _ => new SolidColorBrush(Color.FromRgb(128, 128, 128)),  // Gray
    };

    /// <summary>Formatted location string (e.g. "5:12").</summary>
    public string LocationText => StartLine > 0 ? $"{StartLine}:{StartColumn}" : "";

    /// <summary>Full text for clipboard copy.</summary>
    public string CopyText => StartLine > 0
        ? $"[{RuleId}] {Message} ({StartLine}:{StartColumn})"
        : $"[{RuleId}] {Message}";

    /// <summary>Whether a one-click quick fix is available for this issue.</summary>
    public bool HasQuickFix { get; }

    /// <summary>
    /// Command that applies the quick fix to the current editor document.
    /// Only non-null when <see cref="HasQuickFix"/> is <c>true</c>.
    /// </summary>
    public IRelayCommand? QuickFixCommand { get; }

    public DiagnosticItem(
        string ruleId,
        string message,
        string severity,
        int severityOrder,
        int startOffset,
        int endOffset,
        int startLine = 0,
        int startColumn = 0,
        int endLine = 0,
        int endColumn = 0,
        Func<string>? getSql = null,
        Action<string>? setSql = null,
        (string Description, Func<string, string> Apply)? quickFix = null)
    {
        RuleId = ruleId;
        Message = message;
        Severity = severity;
        SeverityOrder = severityOrder;
        StartOffset = startOffset;
        EndOffset = endOffset;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;

        if (quickFix is not null && getSql is not null && setSql is not null)
        {
            HasQuickFix = true;
            var fix = quickFix.Value;
            QuickFixCommand = new RelayCommand(() =>
            {
                var currentSql = getSql();
                var fixedSql = fix.Apply(currentSql);
                if (fixedSql != currentSql)
                    setSql(fixedSql);
            });
        }
    }
}
