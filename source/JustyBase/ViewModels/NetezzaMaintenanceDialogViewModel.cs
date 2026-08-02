using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustyBase.NetezzaDdl;

namespace JustyBase.ViewModels;

public enum NetezzaMaintenanceDialogKind
{
    Groom,
    GenerateStats
}

public sealed partial class NetezzaMaintenanceDialogViewModel : ObservableObject
{
    public NetezzaMaintenanceDialogKind Kind { get; }
    public string Title { get; }
    public string QualifiedTable { get; }
    public IReadOnlyList<string> GroomModes { get; } = NetezzaMaintenanceSql.GroomModes;
    public IReadOnlyList<string> BackupsetPresets { get; } = NetezzaMaintenanceSql.BackupsetPresets;

    [ObservableProperty]
    private string _selectedGroomMode = NetezzaMaintenanceSql.GroomModes[0];

    [ObservableProperty]
    private string _selectedBackupset = "DEFAULT";

    [ObservableProperty]
    private string _customBackupsetId = "";

    [ObservableProperty]
    private bool _useCustomBackupset;

    public IReadOnlyList<string> StatsModes { get; } = ["EXPRESS", "FULL"];

    [ObservableProperty]
    private string _selectedStatsMode = "EXPRESS";

    [ObservableProperty]
    private string _columnList = "";

    [ObservableProperty]
    private string _previewSql = "";

    public bool Confirmed { get; private set; }
    public string? ResultSql { get; private set; }
    public Action? CloseAction { get; set; }

    public bool IsGroom => Kind == NetezzaMaintenanceDialogKind.Groom;
    public bool IsGenerateStats => Kind == NetezzaMaintenanceDialogKind.GenerateStats;
    public bool IsFullStatistics => IsGenerateStats
        && SelectedStatsMode.Equals("FULL", StringComparison.OrdinalIgnoreCase);

    public NetezzaMaintenanceDialogViewModel(NetezzaMaintenanceDialogKind kind, string qualifiedTable)
    {
        Kind = kind;
        QualifiedTable = qualifiedTable;
        Title = kind == NetezzaMaintenanceDialogKind.Groom
            ? $"GROOM — {qualifiedTable}"
            : $"Generate statistics — {qualifiedTable}";
        RefreshPreview();
    }

    partial void OnSelectedGroomModeChanged(string value) => RefreshPreview();
    partial void OnSelectedBackupsetChanged(string value) => RefreshPreview();
    partial void OnCustomBackupsetIdChanged(string value) => RefreshPreview();
    partial void OnUseCustomBackupsetChanged(bool value) => RefreshPreview();
    partial void OnSelectedStatsModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsFullStatistics));
        RefreshPreview();
    }
    partial void OnColumnListChanged(string value) => RefreshPreview();

    private void RefreshPreview()
    {
        PreviewSql = BuildSql();
    }

    private string BuildSql()
    {
        if (Kind == NetezzaMaintenanceDialogKind.Groom)
        {
            var backup = UseCustomBackupset && !string.IsNullOrWhiteSpace(CustomBackupsetId)
                ? CustomBackupsetId
                : SelectedBackupset;
            return NetezzaMaintenanceSql.BuildGroom(QualifiedTable, SelectedGroomMode, backup);
        }

        var express = SelectedStatsMode.Equals("EXPRESS", StringComparison.OrdinalIgnoreCase);
        return NetezzaMaintenanceSql.BuildGenerateStats(QualifiedTable, express, ColumnList);
    }

    [RelayCommand]
    private void Ok()
    {
        ResultSql = BuildSql();
        Confirmed = true;
        CloseAction?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Confirmed = false;
        ResultSql = null;
        CloseAction?.Invoke();
    }
}
