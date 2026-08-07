using JustyBase.Ai.Ports;
using JustyBase.Common.Contracts;
using JustyBase.ViewModels.Tools;

namespace JustyBase.Services.Ai;

/// <summary>Adapter over the host diagnostics panel for the shared chat tools.</summary>
public sealed class SqlDiagnosticsProviderAdapter : ISqlDiagnosticsProvider
{
    private readonly SqlDiagnosticsViewModel _diagnosticsViewModel;

    public SqlDiagnosticsProviderAdapter(SqlDiagnosticsViewModel diagnosticsViewModel)
    {
        _diagnosticsViewModel = diagnosticsViewModel;
    }

    public IReadOnlyList<ChatDiagnosticItem> Items
        => _diagnosticsViewModel.Items
            .Select(d => new ChatDiagnosticItem(d.RuleId, d.Message, d.Severity, d.StartLine, d.StartColumn))
            .ToList();
}

/// <summary>Adapter over the host logger for the shared chat pipeline.</summary>
public sealed class ChatLoggerAdapter : ISimpleLogger
{
    private readonly JustyBase.PluginCommon.Contracts.ISimpleLogger _logger;

    public ChatLoggerAdapter(JustyBase.PluginCommon.Contracts.ISimpleLogger logger)
    {
        _logger = logger;
    }

    public void TrackError(Exception ex, bool isCrash) => _logger.TrackError(ex, isCrash);
}

/// <summary>Adapter over the host application environment for the shared chat pipeline.</summary>
public sealed class ChatEnvironmentAdapter : IChatEnvironment
{
    public string ConfigDirectory => IGeneralApplicationData.ConfigDirectoryEvo;
}
