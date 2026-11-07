using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using JustyBase.Common.Contracts;
using JustyBase.Helpers;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services;

namespace JustyBase.ViewModels.Tools;

public sealed partial class NetezzaSessionMonitorViewModel : Tool, IDisposable
{
    private readonly NetezzaSessionMonitorService _monitorService;
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly ISimpleLogger _logger;
    private CancellationTokenSource? _refreshCts;

    public NetezzaSessionMonitorViewModel(
        NetezzaSessionMonitorService monitorService,
        IGeneralApplicationData generalApplicationData,
        IMessageForUserTools messageForUserTools,
        ISimpleLogger logger)
    {
        _monitorService = monitorService;
        _generalApplicationData = generalApplicationData;
        _messageForUserTools = messageForUserTools;
        _logger = logger;
        Title = "Sessions";
        Id = "NetezzaSessionMonitor";
        CanClose = true;
        CanPin = true;
        DockCapabilityHelper.SyncOverridesFromFlags(this);
        RefreshConnectionNames();
    }

    public ObservableCollection<string> ConnectionNames { get; } = [];
    public ObservableCollection<NetezzaSessionInfo> Sessions { get; } = [];

    [ObservableProperty]
    public partial string? SelectedConnectionName { get; set; }

    [ObservableProperty]
    public partial NetezzaSessionInfo? SelectedSession { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Select a connection and refresh.";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool AutoRefresh { get; set; }

    partial void OnAutoRefreshChanged(bool value)
    {
        if (value)
        {
            _ = AutoRefreshLoopAsync();
        }
        else
        {
            _refreshCts?.Cancel();
        }
    }

    public void RefreshConnectionNames()
    {
        ConnectionNames.Clear();
        foreach (var key in _generalApplicationData.LoginDataDic.Keys.OrderBy(x => x))
        {
            ConnectionNames.Add(key);
        }

        SelectedConnectionName ??= ConnectionNames.FirstOrDefault();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedConnectionName))
        {
            StatusMessage = "No connection selected.";
            return;
        }

        IsBusy = true;
        try
        {
            var rows = await _monitorService.GetSessionsAsync(SelectedConnectionName);
            Sessions.Clear();
            foreach (var row in rows)
            {
                Sessions.Add(row);
            }

            StatusMessage = $"Loaded {Sessions.Count} session(s).";
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
            StatusMessage = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task KillSelectedAsync()
    {
        if (SelectedSession is null || string.IsNullOrWhiteSpace(SelectedConnectionName))
        {
            return;
        }

        var confirm = await _messageForUserTools.ShowConfirmationDialogAsync(
            $"{SelectedSession.TerminationLabel} {SelectedSession.SessionId} ({SelectedSession.UserName})?\n\n{SelectedSession.KillSql}",
            SelectedSession.TerminationLabel);
        if (!confirm)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _monitorService.KillSessionAsync(SelectedConnectionName, SelectedSession.SessionId);
            StatusMessage = $"{SelectedSession.TerminationLabel} completed for {SelectedSession.SessionId}.";
            await RefreshAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.TrackError(ex, isCrash: false);
            StatusMessage = $"{SelectedSession.TerminationLabel} failed: {ex.Message}";
            _messageForUserTools.ShowSimpleMessageBoxInstance(ex.Message, SelectedSession.TerminationLabel);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AutoRefreshLoopAsync()
    {
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();
        var token = _refreshCts.Token;
        try
        {
            while (AutoRefresh && !token.IsCancellationRequested)
            {
                await RefreshAsync();
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
        }
        catch (OperationCanceledException)
        {
            // Refresh loop stopped or the view model was disposed; nothing to report.
        }
    }

    public void Dispose()
    {
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = null;
        GC.SuppressFinalize(this);
    }
}
