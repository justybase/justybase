using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Common.Tools.ImportHelpers;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.Services;
using JustyBase.Services.Documents;
using System.Collections.ObjectModel;

namespace JustyBase.ViewModels.Documents;

/// <summary>How the imported data is written to the destination.</summary>
public enum ImportDestinationMode
{
    /// <summary>A fresh table is created from the imported columns.</summary>
    CreateNew,

    /// <summary>Rows are inserted into an existing table; columns must be compatible.</summary>
    Existing
}

/// <summary>A single timestamped line in the in-document import progress log.</summary>
public sealed record ImportLogRow(DateTime Time, string Message);

public sealed partial class ImportViewModel : DocumentBaseVM
{
    private readonly IAvaloniaSpecificHelpers _avaloniaSpecificHelpers;
    private readonly IClipboardService _clipboardService;

    public ImportViewModel(
        IAvaloniaSpecificHelpers avaloniaSpecificHelpers,
        IGeneralApplicationData generalApplicationData,
        IMessageForUserTools messageForUserTools,
        IDocumentCloseDecisionService documentCloseDecisionService,
        IActiveDocumentManager activeDocumentManager,
        IDatabaseServiceResolver databaseServiceResolver,
        IClipboardService clipboardService)
        : base(generalApplicationData, messageForUserTools, documentCloseDecisionService, activeDocumentManager)
    {
        _avaloniaSpecificHelpers = avaloniaSpecificHelpers;
        _clipboardService = clipboardService;
        _generalApplicationData = generalApplicationData;
        _messageForUserTools = messageForUserTools;
        _databaseServiceResolver = databaseServiceResolver;
        Title = "Import";
        TabsWarningMessage = "";
        StartEnabled = true;
        OpenFileForImportCommand = new AsyncRelayCommand(async () =>
        {
            IReadOnlyList<IStorageFile> openFile = await ChoseFile();
            if (openFile.Count == 1)
            {
                await OpenMethod(openFile[0].Path.LocalPath);
            }
        });

        DatabaseItems = [];
        SchemaItems = [];
        TableItems = [];
    }

    public ICommand OpenFileForImportCommand { get; set; }

    private async Task<IReadOnlyList<IStorageFile>> ChoseFile()
    {
        return await _avaloniaSpecificHelpers.GetStorageProvider().OpenFilePickerAsync(
            new FilePickerOpenOptions()
            {
                AllowMultiple = false,
                FileTypeFilter = new FilePickerFileType[]
                {
                    new("common files") { Patterns = ["*.xlsx;*.xlsb;*.csv;*.csv.br;*.dat.br;*.csv.gz;*.dat.gz;*.csv.zst;*.dat.zst"] },
                    new("all files") { Patterns = ["*"] }
                }
            });
    }

    #region Source

    [ObservableProperty]
    public partial string ImportFilepath { get; set; }

    /// <summary>true before a source file is opened (hides source-dependent controls).</summary>
    [ObservableProperty]
    public partial bool IsSourceEmpty { get; set; } = true;

    /// <summary>true while an import is executing.</summary>
    [ObservableProperty]
    public partial bool IsImportRunning { get; set; }

    public ObservableCollection<TabItem> ExcelTabsNames { get; set; } = [];

    [ObservableProperty]
    public partial TabItem SelectedTab { get; set; }

    [ObservableProperty]
    public partial bool AllColumnsAsText { get; set; }

    [ObservableProperty]
    public partial string UsingDelimiter { get; set; } = "\\t";

    [ObservableProperty]
    public partial string UsingEncoding { get; set; } = "utf-8";

    [ObservableProperty]
    public partial string UsingMaxRows { get; set; } = "";

    [ObservableProperty]
    public partial string TabsWarningMessage { get; set; }

    #endregion

    #region Detection / validation / columns

    [ObservableProperty]
    public partial bool IsDetecting { get; set; }

    [ObservableProperty]
    public partial bool IsValidating { get; set; }

    [ObservableProperty]
    public partial int ValidationErrorCount { get; set; }

    [ObservableProperty]
    public partial string ValidationSummary { get; set; } = "";

    [ObservableProperty]
    public partial string DetectionStatus { get; set; } = "";

    [ObservableProperty]
    public partial bool StartEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool ContinueEnabled { get; set; }

    public ObservableCollection<ColumnInGrid> ColumnsInGrid { get; set; } = [];

    public ObservableCollection<string[]> PreviewRows { get; set; } = [];

    public Action<string[]>? ActionFromView { get; set; }

    #endregion

    #region Destination

    public ObservableCollection<string> DatabaseItems { get; set; }

    public object SelectedConnection
    {
        get;
        set
        {
            SetProperty(ref field, value);
            DatabaseItems.Clear();
            SchemaItems.Clear();
            TableItems.Clear();
            TableItems.Add(_createNewTxt);
            InvalidateExistingTargetColumns();
            if (SelectedConnectionTyped is null)
            {
                return;
            }

            Interlocked.Increment(ref _schemaListLoadGeneration);
            Interlocked.Increment(ref _tableListLoadGeneration);
            var generation = Interlocked.Increment(ref _databaseListLoadGeneration);
            var connectionName = SelectedConnectionTyped.Name;
            _ = LoadDatabasesForSelectedConnectionAsync(generation, connectionName);
        }
    }

    public ConnectionItem? SelectedConnectionTyped => SelectedConnection as ConnectionItem;

    public string SelectedDatabase
    {
        get;
        set
        {
            SetProperty(ref field, value);
            SchemaItems.Clear();
            TableItems.Clear();
            TableItems.Add(_createNewTxt);
            InvalidateExistingTargetColumns();
            if (SelectedConnectionTyped is null || string.IsNullOrWhiteSpace(SelectedDatabase))
            {
                return;
            }

            Interlocked.Increment(ref _tableListLoadGeneration);
            var generation = Interlocked.Increment(ref _schemaListLoadGeneration);
            var connectionName = SelectedConnectionTyped.Name;
            var database = SelectedDatabase;
            _ = LoadSchemasForSelectedDatabaseAsync(generation, connectionName, database);
        }
    }

    public ObservableCollection<string> SchemaItems { get; set; }

    public string SelectedSchema
    {
        get;
        set
        {
            SetProperty(ref field, value);
            TableItems.Clear();
            TableItems.Add(_createNewTxt);
            InvalidateExistingTargetColumns();
            if (SelectedConnectionTyped is null
                || string.IsNullOrWhiteSpace(SelectedDatabase)
                || string.IsNullOrWhiteSpace(SelectedSchema))
            {
                return;
            }

            var generation = Interlocked.Increment(ref _tableListLoadGeneration);
            var connectionName = SelectedConnectionTyped.Name;
            var database = SelectedDatabase;
            var schema = SelectedSchema;
            _ = LoadTablesForSelectedSchemaAsync(generation, connectionName, database, schema);
        }
    }

    public ObservableCollection<string> TableItems { get; set; }

    [ObservableProperty]
    public partial string SelectedTableText { get; set; }

    private readonly string _createNewTxt = "[CREATE NEW TABLE]";

    /// <summary>Create-new vs existing-table destination mode.</summary>
    [ObservableProperty]
    public partial ImportDestinationMode DestinationMode { get; set; } = ImportDestinationMode.CreateNew;

    public bool IsCreateNewMode
    {
        get => DestinationMode == ImportDestinationMode.CreateNew;
        set
        {
            if (value)
            {
                DestinationMode = ImportDestinationMode.CreateNew;
            }
        }
    }

    public bool IsExistingMode
    {
        get => DestinationMode == ImportDestinationMode.Existing;
        set
        {
            if (value)
            {
                DestinationMode = ImportDestinationMode.Existing;
            }
        }
    }

    partial void OnDestinationModeChanged(ImportDestinationMode value)
    {
        InvalidateExistingTargetColumns();
    }

    /// <summary>
    /// Drops a previously computed existing-table column mapping whenever the destination
    /// or the source changes — otherwise Import would reuse table A's mapping for table B.
    /// </summary>
    private void InvalidateExistingTargetColumns()
    {
        CompatibilitySummary = "";
        HasCompatibilityErrors = false;
        _existingTargetColumnNames = null;
        UpdateStartEnabled();
    }

    partial void OnSelectedTableTextChanged(string value) => InvalidateExistingTargetColumns();

    [ObservableProperty]
    public partial bool IsCheckingCompatibility { get; set; }

    [ObservableProperty]
    public partial string CompatibilitySummary { get; set; } = "";

    [ObservableProperty]
    public partial bool HasCompatibilityErrors { get; set; }

    #endregion

    #region Progress (in-document)

    public ObservableCollection<ImportLogRow> ImportLog { get; set; } = [];

    [ObservableProperty]
    public partial int ProgressValue { get; set; }

    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; set; } = true;

    [ObservableProperty]
    public partial string ProgressStatus { get; set; } = "";

    [ObservableProperty]
    public partial string ResultSql { get; set; } = "";

    [ObservableProperty]
    public partial bool IsProgressVisible { get; set; }

    #endregion

    #region Bulk type actions

    [RelayCommand]
    private void ApplyAllAsText()
    {
        if (ColumnsInGrid.Count == 0)
        {
            return;
        }

        foreach (ColumnInGrid column in ColumnsInGrid)
        {
            column.ApplyType(DbSimpleType.Nvarchar);
        }

        RevalidateAfterBulkChange();
    }

    [RelayCommand]
    private void RevertAllToDetected()
    {
        if (ColumnsInGrid.Count == 0)
        {
            return;
        }

        foreach (ColumnInGrid column in ColumnsInGrid)
        {
            column.ResetToDetected();
        }

        RevalidateAfterBulkChange();
    }

    private void RevalidateAfterBulkChange()
    {
        if (_currentImportFromExcelFile is null || _importActive)
        {
            return;
        }

        ValidationErrorCount = 1;
        ValidationSummary = "Validation is required after a bulk type change.";
        IsValidating = true;
        int generation = Interlocked.Increment(ref _validationGeneration);
        ImportFromExcelFile importFile = _currentImportFromExcelFile;
        string sheetName = SelectedTab?.TabName ?? "selected sheet";
        CancellationTokenSource validationCancellation = BeginValidationCancellation();
        ApplySelectedSheetsToImport(importFile);
        _ = ValidateAfterGridTypeChangedAsync(importFile, sheetName, generation, validationCancellation);
    }

    #endregion

    #region Copy / insert result

    [RelayCommand]
    private async Task CopySelectAsync()
    {
        if (string.IsNullOrWhiteSpace(ResultSql))
        {
            return;
        }

        await _clipboardService.SetTextAsync(ResultSql);
        AddProgressLine($"SELECT copied to clipboard ({ResultSql.Length:N0} chars).");
    }

    [RelayCommand]
    private void InsertSelectIntoEditor()
    {
        if (string.IsNullOrWhiteSpace(ResultSql))
        {
            return;
        }

        ActiveDocumentManager.InsertTextToActiveDocument(ResultSql + "\n", rawMode: true);
        AddProgressLine("SELECT inserted into the SQL editor.");
    }

    #endregion
}
