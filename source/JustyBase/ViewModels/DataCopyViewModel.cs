using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustyBase.Common.Contracts;
using JustyBase.Services.DataCopy;

namespace JustyBase.ViewModels;

/// <summary>Dialog model for copying one table between saved connections.</summary>
public sealed partial class DataCopyViewModel : ObservableObject
{
    private readonly DataCopyService _dataCopyService;
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IMessageForUserTools _messageForUserTools;
    private CancellationTokenSource? _cancellation;

    public DataCopyViewModel(
        DataCopyService dataCopyService,
        IGeneralApplicationData generalApplicationData,
        IMessageForUserTools messageForUserTools,
        string sourceConnectionName,
        string sourceDatabase,
        string sourceSchema,
        string sourceTable,
        bool isView)
    {
        _dataCopyService = dataCopyService;
        _generalApplicationData = generalApplicationData;
        _messageForUserTools = messageForUserTools;

        SourceConnectionName = sourceConnectionName;
        SourceDatabase = sourceDatabase;
        SourceSchema = sourceSchema;
        SourceTable = sourceTable;
        IsView = isView;
        TargetSchema = sourceSchema;
        TargetTable = sourceTable;
        CreateTarget = !isView;
        Status = isView
            ? "Views are copied through SELECT *; the target is created as a table."
            : "Preview the mapping, then copy the rows.";

        foreach (string connection in generalApplicationData.LoginDataDic.Keys.OrderBy(name => name))
        {
            ConnectionNames.Add(connection);
        }

        SelectedTargetConnection = ConnectionNames.FirstOrDefault(
            name => !string.Equals(name, sourceConnectionName, StringComparison.OrdinalIgnoreCase));
        if (SelectedTargetConnection is not null)
        {
            TargetDatabase = generalApplicationData.LoginDataDic[SelectedTargetConnection].Database ?? sourceDatabase;
        }
    }

    public ObservableCollection<string> ConnectionNames { get; } = [];

    public string SourceConnectionName { get; }
    public string SourceDatabase { get; }
    public string SourceSchema { get; }
    public string SourceTable { get; }
    public bool IsView { get; }

    public string SourceDescription
        => $"{SourceConnectionName}: {SourceDatabase}.{SourceSchema}.{SourceTable}";

    public Action? CloseAction { get; set; }

    [ObservableProperty]
    public partial string? SelectedTargetConnection { get; set; }

    [ObservableProperty]
    public partial string TargetDatabase { get; set; } = "";

    [ObservableProperty]
    public partial string TargetSchema { get; set; } = "";

    [ObservableProperty]
    public partial string TargetTable { get; set; } = "";

    [ObservableProperty]
    public partial bool CreateTarget { get; set; }

    [ObservableProperty]
    public partial bool ReplaceTarget { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial string ColumnsSummary { get; set; } = "";

    partial void OnSelectedTargetConnectionChanged(string? value)
    {
        if (value is not null
            && _generalApplicationData.LoginDataDic.TryGetValue(value, out var login)
            && !string.IsNullOrWhiteSpace(login.Database))
        {
            TargetDatabase = login.Database;
        }
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (!TryBuildRequest(out var request))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var preview = await _dataCopyService.PreviewAsync(request);
            ColumnsSummary = string.Join(
                Environment.NewLine,
                preview.Columns.Select(column =>
                    $"{column.SourceName} → {column.TargetName} : {column.TargetType}"));
            string target = preview.TargetExists ? "target table exists" : "target table will be created";
            Status = $"{preview.Columns.Count} column(s); {target}.";
            if (preview.Warnings.Count > 0)
            {
                Status += Environment.NewLine + string.Join(Environment.NewLine, preview.Warnings);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = $"Preview failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (!TryBuildRequest(out var request))
        {
            return;
        }

        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        IsBusy = true;
        Status = "Copying…";
        try
        {
            var result = await _dataCopyService.CopyAsync(
                request,
                rows => _messageForUserTools.DispatcherActionInstance(() =>
                {
                    Status = $"Copied {rows:N0} row(s)…";
                }),
                token);
            Status = $"Copied {result.RowsCopied:N0} row(s) in {result.Elapsed:g}.";
        }
        catch (OperationCanceledException)
        {
            Status = "Copy cancelled.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = $"Copy failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancellation?.Cancel();
    }

    [RelayCommand]
    private void Close()
    {
        _cancellation?.Cancel();
        CloseAction?.Invoke();
    }

    private bool TryBuildRequest(out DataCopyRequest request)
    {
        request = null!;
        if (string.IsNullOrWhiteSpace(SelectedTargetConnection)
            || string.IsNullOrWhiteSpace(TargetTable))
        {
            Status = "Select a target connection and enter a table name.";
            return false;
        }

        request = new DataCopyRequest(
            SourceConnectionName,
            SourceDatabase,
            SourceSchema,
            SourceTable,
            SelectedTargetConnection,
            TargetDatabase,
            TargetSchema,
            TargetTable,
            CreateTarget,
            ReplaceTarget);
        return true;
    }
}
