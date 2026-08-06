using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustyBase.Common.Contracts;
using JustyBase.Common.Models;
using JustyBase.Common.Tools;
using JustyBase.Common.Tools.ImportHelpers;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginCommons;
using JustyBase.Helpers.Shared;
using JustyBase.Helpers;
using JustyBase.ImportExport.Import;
using JustyBase.Services.Documents;
using System.Collections.ObjectModel;
using System.Text;
using DatabaseTypeChooser = JustyBase.Common.Tools.ImportHelpers.DatabaseTypeChooser;

namespace JustyBase.ViewModels.Documents;

public sealed partial class ImportViewModel
{
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly IDatabaseServiceResolver _databaseServiceResolver;

    private readonly Dictionary<string, ImportFromExcelFile> _importFromExcelFilesClasses = [];
    private ImportFromExcelFile? _currentImportFromExcelFile;
    private bool _importActive;
    private bool _isQuickImport;
    private string[]? _existingTargetColumnNames;

    private int _sheetDetectionGeneration;
    private int _validationGeneration;
    private CancellationTokenSource? _validationCancellation;

    private CancellationTokenSource BeginValidationCancellation()
    {
        var current = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _validationCancellation, current);
        if (previous is not null)
            previous.Cancel();
        return current;
    }

    private void CancelCurrentValidation()
    {
        var current = Interlocked.Exchange(ref _validationCancellation, null);
        if (current is not null)
            current.Cancel();
    }

    private bool IsCurrentValidation(ImportFromExcelFile importFile, int generation, string sheetName, CancellationToken cancellationToken)
        => !cancellationToken.IsCancellationRequested
            && generation == Volatile.Read(ref _validationGeneration)
            && ReferenceEquals(importFile, _currentImportFromExcelFile)
            && string.Equals(SelectedTab?.TabName, sheetName, StringComparison.Ordinal);

    partial void OnSelectedTabChanged(TabItem value)
    {
        CancelCurrentValidation();
        Interlocked.Increment(ref _validationGeneration);
        int generation = Interlocked.Increment(ref _sheetDetectionGeneration);
        ColumnsInGrid.Clear();
        PreviewRows.Clear();
        ValidationErrorCount = 0;
        ValidationSummary = "";
        if (value is not null && value.TabOk && !_importActive)
        {
            _ = DetectAndShowCurrentSheetAsync(generation);
        }
        else
        {
            UpdateStartEnabled();
        }
    }

    partial void OnAllColumnsAsTextChanged(bool value)
    {
        if (_currentImportFromExcelFile is not null)
        {
            CancelCurrentValidation();
            Interlocked.Increment(ref _validationGeneration);
            _currentImportFromExcelFile.TreatAllColumnsAsText = value;
            if (SelectedTab is not null && !_importActive)
            {
                _ = DetectAndShowCurrentSheetAsync(Interlocked.Increment(ref _sheetDetectionGeneration));
            }
        }
    }

    partial void OnIsDetectingChanged(bool value) => UpdateStartEnabled();
    partial void OnIsValidatingChanged(bool value) => UpdateStartEnabled();
    partial void OnValidationErrorCountChanged(int value) => UpdateStartEnabled();

    private void UpdateStartEnabled()
    {
        if (_currentImportFromExcelFile is null)
        {
            StartEnabled = !_importActive && !IsDetecting && !IsValidating;
            return;
        }

        bool existingReady = DestinationMode != ImportDestinationMode.Existing
            || (!string.IsNullOrWhiteSpace(SelectedTableText)
                && SelectedTableText != _createNewTxt
                && !HasCompatibilityErrors);
        StartEnabled = !_importActive
            && !IsDetecting
            && !IsValidating
            && ColumnsInGrid.Count > 0
            && ValidationErrorCount == 0
            && existingReady;
    }

    private async Task LoadDatabasesForSelectedConnectionAsync(int generation, string connectionName)
    {
        List<string> databases;
        try
        {
            databases = await Task.Run(() =>
                LoadHierarchyWithSchemaRefreshFallback(connectionName, s => s?.GetDatabases("").ToList() ?? []));
        }
        catch (Exception ex)
        {
            ReportHierarchyLoadFailure(
                generation,
                Volatile.Read(ref _databaseListLoadGeneration),
                $"Could not load databases for connection '{connectionName}'.",
                ex);
            return;
        }

        if (generation != Volatile.Read(ref _databaseListLoadGeneration)
            || !string.Equals(SelectedConnectionTyped?.Name, connectionName, StringComparison.Ordinal))
        {
            return;
        }

        DatabaseItems.Clear();
        foreach (var item in databases)
        {
            DatabaseItems.Add(item);
        }
    }

    private async Task LoadSchemasForSelectedDatabaseAsync(int generation, string connectionName, string database)
    {
        List<string> schemas;
        try
        {
            schemas = await Task.Run(() =>
                LoadHierarchyWithSchemaRefreshFallback(connectionName, s => s?.GetSchemas(database, "").ToList() ?? []));
        }
        catch (Exception ex)
        {
            ReportHierarchyLoadFailure(
                generation,
                Volatile.Read(ref _schemaListLoadGeneration),
                $"Could not load schemas for database '{database}'.",
                ex);
            return;
        }

        if (generation != Volatile.Read(ref _schemaListLoadGeneration)
            || !string.Equals(SelectedConnectionTyped?.Name, connectionName, StringComparison.Ordinal)
            || !string.Equals(SelectedDatabase, database, StringComparison.Ordinal))
        {
            return;
        }

        SchemaItems.Clear();
        foreach (var item in schemas)
        {
            SchemaItems.Add(item);
        }
    }

    private async Task LoadTablesForSelectedSchemaAsync(int generation, string connectionName, string database, string schema)
    {
        List<string> tables;
        try
        {
            tables = await Task.Run(() =>
            {
                var service = _databaseServiceResolver.GetDatabaseService(_generalApplicationData, connectionName);
                return service?
                    .GetDbObjects(database, schema, "", TypeInDatabaseEnum.Table)
                    .Select(o => o.Name)
                    .ToList() ?? [];
            });
        }
        catch (Exception ex)
        {
            ReportHierarchyLoadFailure(
                generation,
                Volatile.Read(ref _tableListLoadGeneration),
                $"Could not load tables for schema '{schema}'.",
                ex);
            return;
        }

        if (generation != Volatile.Read(ref _tableListLoadGeneration)
            || !string.Equals(SelectedConnectionTyped?.Name, connectionName, StringComparison.Ordinal)
            || !string.Equals(SelectedDatabase, database, StringComparison.Ordinal)
            || !string.Equals(SelectedSchema, schema, StringComparison.Ordinal))
        {
            return;
        }

        TableItems.Clear();
        TableItems.Add(_createNewTxt);
        foreach (var item in tables)
        {
            TableItems.Add(item);
        }
    }

    private List<string> LoadHierarchyWithSchemaRefreshFallback(
        string connectionName,
        Func<IDatabaseService?, List<string>> query)
    {
        var service = _databaseServiceResolver.GetDatabaseService(_generalApplicationData, connectionName);
        List<string> result = query(service);

        if (result.Count == 0
            && (service?.ConnectedLevel ?? DatabaseConnectedLevel.NotConnected) < DatabaseConnectedLevel.ConnectedDatabaseObjects)
        {
            service = _databaseServiceResolver.GetDatabaseService(_generalApplicationData, connectionName, forceRefresh: true);
            result = query(service);
        }

        return result;
    }

    private void ReportHierarchyLoadFailure(int loadGeneration, int currentGeneration, string title, Exception ex)
    {
        _generalApplicationData.GlobalLoggerObject.TrackError(ex, isCrash: false);
        if (loadGeneration != currentGeneration)
        {
            return;
        }

        _messageForUserTools.DispatcherActionInstance(() =>
            _messageForUserTools.ShowSimpleMessageBoxInstance($"{title}\n\n{ex.Message}", "Import"));
    }

    private int _databaseListLoadGeneration;
    private int _schemaListLoadGeneration;
    private int _tableListLoadGeneration;

    public void ApplyImportContext(string? connectionName, string? database, string? schema, string? table)
    {
        if (!string.IsNullOrWhiteSpace(connectionName))
        {
            var connection = ConnectionsList?.FirstOrDefault(c =>
                string.Equals(c.Name, connectionName, StringComparison.OrdinalIgnoreCase));
            if (connection is not null)
            {
                SelectedConnection = connection;
            }
        }

        if (!string.IsNullOrWhiteSpace(database))
        {
            if (!DatabaseItems.Contains(database))
            {
                DatabaseItems.Add(database);
            }

            SelectedDatabase = database;
        }

        if (!string.IsNullOrWhiteSpace(schema))
        {
            if (!SchemaItems.Contains(schema))
            {
                SchemaItems.Add(schema);
            }

            SelectedSchema = schema;
        }

        if (!string.IsNullOrWhiteSpace(table))
        {
            if (!TableItems.Contains(table))
            {
                TableItems.Add(table);
            }

            SelectedTableText = table;
            Title = $"Import: {table}";
        }
        else
        {
            Title = "Import";
        }
    }

    private ImportUsingOptions BuildUsingOptions()
    {
        string delimiter = UsingDelimiter?.Trim() ?? "\\t";
        if (delimiter is "\\t" or "tab" or "TAB")
        {
            delimiter = "\t";
        }
        else if (delimiter.Equals("comma", StringComparison.OrdinalIgnoreCase))
        {
            delimiter = ",";
        }
        else if (delimiter.Length >= 2 && delimiter.StartsWith('\'') && delimiter.EndsWith('\''))
        {
            delimiter = delimiter[1..^1];
        }

        int? maxRows = null;
        if (int.TryParse(UsingMaxRows, out int parsed) && parsed > 0)
        {
            maxRows = parsed;
        }

        return new ImportUsingOptions
        {
            Delimiter = string.IsNullOrEmpty(delimiter) ? "\t" : delimiter,
            EncodingName = string.IsNullOrWhiteSpace(UsingEncoding) ? "utf-8" : UsingEncoding.Trim(),
            MaxRows = maxRows
        };
    }

    private Encoding ResolveSourceEncoding()
    {
        try
        {
            return AdvancedExportOptions.ParseEnconding(
                string.IsNullOrWhiteSpace(UsingEncoding) ? "UTF-8" : UsingEncoding.Trim());
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private async Task OpenMethod(string filePath)
    {
        ImportFilepath = filePath;
        IsSourceEmpty = false;

        var curentImportFromFile = new ImportFromExcelFile(x => _messageForUserTools.ShowSimpleMessageBoxInstance(x), _generalApplicationData.GlobalLoggerObject)
        {
            FilePath = filePath,
            TreatAllColumnsAsText = this.AllColumnsAsText
        };
        _importFromExcelFilesClasses[filePath] = curentImportFromFile;
        _currentImportFromExcelFile = curentImportFromFile;

        if (string.IsNullOrWhiteSpace(curentImportFromFile.FilePath))
        {
            return;
        }

        var encoding = ResolveSourceEncoding();
        var initSuccess = await Task.Run(() =>
        {
            if (!curentImportFromFile.InitImport(encoding: encoding))
            {
                curentImportFromFile.DoFileDispose();
                return false;
            }
            return true;
        });
        if (!initSuccess)
        {
            _importFromExcelFilesClasses.Remove(filePath);
            if (ReferenceEquals(_currentImportFromExcelFile, curentImportFromFile))
                _currentImportFromExcelFile = null;
            UpdateStartEnabled();
            return;
        }

        ClearTypeSelection();
        ExcelTabsNames.Clear();
        for (int i = 0; i < curentImportFromFile.SheetNamesToImport.Count; i++)
        {
            string item = curentImportFromFile.SheetNamesToImport[i];
            ExcelTabsNames.Add(new TabItem() { TabName = item, TabOk = (i == 0), TabOkChanged = OnTabOkChanged });
        }
        SelectedTab = ExcelTabsNames.Count > 0 ? ExcelTabsNames[0] : null;
        TabsWarningMessage = "";
    }

    private void OnTabOkChanged(TabItem item)
    {
        if (!_importActive)
        {
            CancelCurrentValidation();
            Interlocked.Increment(ref _validationGeneration);
        }

        if (!item.TabOk && ReferenceEquals(item, SelectedTab))
        {
            SelectedTab = ExcelTabsNames.FirstOrDefault(t => t.TabOk);
            return;
        }

        if (item.TabOk && ReferenceEquals(item, SelectedTab) && !_importActive)
        {
            _ = DetectAndShowCurrentSheetAsync(Interlocked.Increment(ref _sheetDetectionGeneration));
        }
    }

    private async Task DetectAndShowCurrentSheetAsync(int generation = -1)
    {
        var importFile = _currentImportFromExcelFile;
        var tab = SelectedTab;
        if (importFile is null || tab is null || !tab.TabOk)
        {
            return;
        }

        string sheetName = tab.TabName;
        if (generation < 0)
            generation = Volatile.Read(ref _sheetDetectionGeneration);

        CancellationTokenSource validationCancellation = BeginValidationCancellation();
        CancellationToken cancellationToken = validationCancellation.Token;
        int validationGeneration = 0;
        IsDetecting = true;
        try
        {
            var chooser = await importFile.DetectSheetAsync(
                sheetName,
                s => _messageForUserTools.DispatcherActionInstance(() => DetectionStatus = s),
                cancellationToken);
            if (chooser is null
                || generation != Volatile.Read(ref _sheetDetectionGeneration)
                || !ReferenceEquals(importFile, _currentImportFromExcelFile)
                || !string.Equals(SelectedTab?.TabName, sheetName, StringComparison.Ordinal))
            {
                return;
            }

            DetectionStatus = "";
            PopulateColumnsFromChooser(chooser);
            ApplySelectedSheetsToImport(importFile);

            IsValidating = true;
            validationGeneration = Interlocked.Increment(ref _validationGeneration);
            IReadOnlyList<ImportValidationError> errors = await importFile.ValidateSelectedSheetsAsync(
                cancellationToken: cancellationToken);
            if (IsCurrentValidation(importFile, validationGeneration, sheetName, cancellationToken)
                && generation == Volatile.Read(ref _sheetDetectionGeneration))
            {
                ApplyValidationErrors(chooser, errors);
            }
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested
            || generation != Volatile.Read(ref _sheetDetectionGeneration)
            || (validationGeneration != 0 && validationGeneration != Volatile.Read(ref _validationGeneration)))
        {
        }
        catch (Exception ex)
        {
            bool isCurrentSheet = !cancellationToken.IsCancellationRequested
                && generation == Volatile.Read(ref _sheetDetectionGeneration)
                && ReferenceEquals(importFile, _currentImportFromExcelFile)
                && string.Equals(SelectedTab?.TabName, sheetName, StringComparison.Ordinal);
            if (isCurrentSheet)
            {
                _generalApplicationData.GlobalLoggerObject.TrackError(ex, isCrash: false);
                _messageForUserTools.DispatcherActionInstance(() =>
                {
                    if (!cancellationToken.IsCancellationRequested
                        && generation == Volatile.Read(ref _sheetDetectionGeneration)
                        && ReferenceEquals(importFile, _currentImportFromExcelFile)
                        && string.Equals(SelectedTab?.TabName, sheetName, StringComparison.Ordinal))
                    {
                        IsValidating = false;
                        IsDetecting = false;
                        ValidationErrorCount = 1;
                        ValidationSummary = $"Validation failed for sheet '{sheetName}': {ex.Message}";
                        StartEnabled = false;
                    }
                });
            }
        }
        finally
        {
            if (generation == Volatile.Read(ref _sheetDetectionGeneration)
                && ReferenceEquals(validationCancellation, Volatile.Read(ref _validationCancellation)))
            {
                _messageForUserTools.DispatcherActionInstance(() =>
                {
                    if (generation == Volatile.Read(ref _sheetDetectionGeneration)
                        && ReferenceEquals(validationCancellation, Volatile.Read(ref _validationCancellation)))
                    {
                        IsValidating = false;
                        IsDetecting = false;
                        UpdateStartEnabled();
                    }
                });
            }
        }
    }

    private void PopulateColumnsFromChooser(DatabaseTypeChooser chooser)
    {
        if (chooser.NormalizedColumnHeaderNames is null || chooser.ColumnTypesBestMatch is null)
        {
            return;
        }

        var headers = chooser.NormalizedColumnHeaderNames;
        var types = chooser.ColumnTypesBestMatch;
        var detectedTypes = chooser.DetectedColumnTypes ?? types;
        ColumnsInGrid.Clear();
        _isPopulatingColumns = true;
        try
        {
            for (int i = 0; i < types.Length; i++)
            {
                int rawLength = chooser.RawValueLengths is not null && i < chooser.RawValueLengths.Length ? chooser.RawValueLengths[i] : 0;
                ColumnsInGrid.Add(new ColumnInGrid(headers[i], detectedTypes[i], types, i, rawLength, FormatType, OnGridTypeChanged));
            }
        }
        finally
        {
            _isPopulatingColumns = false;
        }
        PopulatePreview(headers, chooser.PreviewRows);
        ApplyValidationErrors(chooser, chooser.ValidationErrors);
    }

    private void PopulateColumnsFromJob(DbImportJob? importJob)
    {
        if (importJob?.ColumnHeadersNames is null || importJob.ColumnTypesBestMatch.Length == 0)
        {
            return;
        }

        var headers = importJob.ColumnHeadersNames;
        var types = importJob.ColumnTypesBestMatch;
        ColumnsInGrid.Clear();
        _isPopulatingColumns = true;
        try
        {
            for (int i = 0; i < types.Length; i++)
            {
                ColumnsInGrid.Add(new ColumnInGrid(headers[i], types[i], types, i, 0, FormatType));
            }
        }
        finally
        {
            _isPopulatingColumns = false;
        }
        PopulatePreview(headers, importJob.PreviewRows);
    }

    private void PopulatePreview(IReadOnlyList<string> headers, IReadOnlyList<string[]>? rows)
    {
        PreviewRows.Clear();
        if (rows is not null)
        {
            foreach (var row in rows)
            {
                PreviewRows.Add(row);
            }
        }
        _messageForUserTools.DispatcherActionInstance(() => ActionFromView?.Invoke(headers.ToArray()));
    }

    private void ApplySelectedSheetsToImport(ImportFromExcelFile importFile)
    {
        importFile.SheetNamesToImport = ExcelTabsNames
            .Where(t => t.TabOk)
            .Select(t => t.TabName)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private void ApplyValidationErrors(DatabaseTypeChooser chooser, IEnumerable<ImportValidationError> errors)
    {
        ImportValidationError[] sheetErrors = errors
            .Where(e => string.Equals(e.SheetName, SelectedTab?.TabName, StringComparison.Ordinal))
            .ToArray();

        foreach (ColumnInGrid column in ColumnsInGrid)
            column.ValidationError = "";

        foreach (IGrouping<int, ImportValidationError> group in sheetErrors.GroupBy(e => e.ColumnIndex))
        {
            ColumnInGrid? column = ColumnsInGrid.ElementAtOrDefault(group.Key);
            if (column is not null)
                column.ValidationError = group.First().Message;
        }

        int cachedErrorCount = _currentImportFromExcelFile is null
            ? errors.Count()
            : ExcelTabsNames
                .Where(t => t.TabOk)
                .Select(t => _currentImportFromExcelFile.GetTypeChooser(t.TabName))
                .Where(c => c is not null)
                .Sum(c => c!.ValidationErrors.Count);
        ValidationErrorCount = cachedErrorCount;
        ValidationSummary = ValidationErrorCount == 0
            ? ""
            : $"Validation failed for {ValidationErrorCount:N0} value(s). {errors.FirstOrDefault()?.ToString() ?? "See the selected sheet and column."}";
        chooser.SetValidationErrors(errors.Where(e => string.Equals(e.SheetName, SelectedTab?.TabName, StringComparison.Ordinal)));
        UpdateStartEnabled();
    }

    private bool _isPopulatingColumns;

    private void OnGridTypeChanged()
    {
        if (_currentImportFromExcelFile is null || _importActive || _isPopulatingColumns)
            return;

        ValidationErrorCount = 1;
        ValidationSummary = "Validation is required after changing a selected type.";
        IsValidating = true;
        int generation = Interlocked.Increment(ref _validationGeneration);
        ImportFromExcelFile importFile = _currentImportFromExcelFile;
        string sheetName = SelectedTab?.TabName ?? "selected sheet";
        CancellationTokenSource validationCancellation = BeginValidationCancellation();
        ApplySelectedSheetsToImport(importFile);
        _ = ValidateAfterGridTypeChangedAsync(importFile, sheetName, generation, validationCancellation);
    }

    private async Task ValidateAfterGridTypeChangedAsync(
        ImportFromExcelFile importFile,
        string sheetName,
        int generation,
        CancellationTokenSource validationCancellation)
    {
        CancellationToken cancellationToken = validationCancellation.Token;
        try
        {
            IReadOnlyList<ImportValidationError> errors = await importFile.ValidateSelectedSheetsAsync(
                cancellationToken: cancellationToken);
            if (!IsCurrentValidation(importFile, generation, sheetName, cancellationToken))
                return;

            _messageForUserTools.DispatcherActionInstance(() =>
            {
                if (IsCurrentValidation(importFile, generation, sheetName, cancellationToken)
                    && importFile.GetTypeChooser(sheetName) is { } chooser)
                    ApplyValidationErrors(chooser, errors);
            });
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested
            || generation != Volatile.Read(ref _validationGeneration)
            || !ReferenceEquals(importFile, _currentImportFromExcelFile))
        {
        }
        catch (Exception ex)
        {
            if (!IsCurrentValidation(importFile, generation, sheetName, cancellationToken))
                return;

            _generalApplicationData.GlobalLoggerObject.TrackError(ex, isCrash: false);
            _messageForUserTools.DispatcherActionInstance(() =>
            {
                if (IsCurrentValidation(importFile, generation, sheetName, cancellationToken))
                {
                    IsValidating = false;
                    ValidationErrorCount = 1;
                    ValidationSummary = $"Validation failed for sheet '{sheetName}': {ex.Message}";
                    StartEnabled = false;
                }
            });
            return;
        }

        if (IsCurrentValidation(importFile, generation, sheetName, cancellationToken))
        {
            _messageForUserTools.DispatcherActionInstance(() =>
            {
                if (IsCurrentValidation(importFile, generation, sheetName, cancellationToken))
                {
                    IsValidating = false;
                    UpdateStartEnabled();
                }
            });
        }
    }

    private void ClearTypeSelection()
    {
        ColumnsInGrid.Clear();
        PreviewRows.Clear();
        ValidationErrorCount = 0;
        ValidationSummary = "";
        _messageForUserTools.DispatcherActionInstance(() => ActionFromView?.Invoke([]));
    }

    private string FormatType(DbTypeWithSize type)
        => type.ToString(SelectedConnectionTyped?.DatabaseType ?? DatabaseTypeEnum.NetezzaSQL);

    [ObservableProperty]
    public partial int SelIndexOpt { get; set; }

    partial void OnSelIndexOptChanged(int value)
    {
        if (value == 1 && !_importActive)
        {
            _ = DetectAndShowCurrentSheetAsync();
        }
    }

    #region Progress (in-document)

    private void ResetProgress()
    {
        _messageForUserTools.DispatcherActionInstance(() =>
        {
            ImportLog.Clear();
            ProgressValue = 0;
            IsProgressIndeterminate = true;
            ProgressStatus = "";
            ResultSql = "";
            IsProgressVisible = true;
        });
    }

    private void AddProgressLine(string message)
    {
        _messageForUserTools.DispatcherActionInstance(() =>
        {
            ImportLog.Add(new ImportLogRow(DateTime.Now, message));
            ProgressStatus = message;
            IsProgressVisible = true;
        });
    }

    private void SetProgress(int value, bool indeterminate)
    {
        _messageForUserTools.DispatcherActionInstance(() =>
        {
            ProgressValue = value;
            IsProgressIndeterminate = indeterminate;
        });
    }

    private void SetCompleted(string tableName, IReadOnlyList<string>? headers)
    {
        _messageForUserTools.DispatcherActionInstance(() =>
        {
            ProgressValue = 100;
            IsProgressIndeterminate = false;
            ProgressStatus = "Completed";
            ImportLog.Add(new ImportLogRow(DateTime.Now, "Completed."));
            ResultSql = headers is { Count: > 0 }
                ? ImportSelectSqlBuilder.BuildAliasedColumnSelect(tableName, headers)
                : $"SELECT * FROM {tableName} LIMIT 100";
            IsProgressVisible = true;
        });
    }

    #endregion

    #region Existing-table compatibility

    [RelayCommand]
    private async Task CheckCompatibilityAsync()
    {
        if (SelectedConnectionTyped is null
            || string.IsNullOrWhiteSpace(SelectedDatabase)
            || string.IsNullOrWhiteSpace(SelectedSchema)
            || string.IsNullOrWhiteSpace(SelectedTableText)
            || SelectedTableText == _createNewTxt)
        {
            CompatibilitySummary = "Select a connection, database, schema and an existing table first.";
            HasCompatibilityErrors = true;
            _existingTargetColumnNames = null;
            UpdateStartEnabled();
            return;
        }

        var importFile = _currentImportFromExcelFile;
        var chooser = SelectedTab is null ? null : importFile?.GetTypeChooser(SelectedTab.TabName);
        if (chooser?.NormalizedColumnHeaderNames is null || chooser.ColumnTypesBestMatch is null)
        {
            CompatibilitySummary = "Detect the source sheet first (open the source file and select the sheet).";
            HasCompatibilityErrors = true;
            _existingTargetColumnNames = null;
            UpdateStartEnabled();
            return;
        }

        IsCheckingCompatibility = true;
        CompatibilitySummary = "";
        try
        {
            var service = await Task.Run(() =>
                _databaseServiceResolver.GetDatabaseService(_generalApplicationData, SelectedConnectionTyped.Name, delayCache: false));
            if (service is null)
            {
                CompatibilitySummary = "Could not resolve the database service.";
                HasCompatibilityErrors = true;
                _existingTargetColumnNames = null;
                UpdateStartEnabled();
                return;
            }

            var targetColumns = await Task.Run(() =>
                service.GetColumns(SelectedDatabase, SelectedSchema, SelectedTableText, "").ToList());

            var sourceHeaders = chooser.NormalizedColumnHeaderNames;
            var sourceKinds = chooser.ColumnTypesBestMatch.Select(static t => DatabaseTypeChooser.MapKind(t.DatabaseTypeSimple)).ToArray();

            var (summary, targetNames, hasErrors) = BuildCompatibilityReport(
                sourceHeaders,
                sourceKinds,
                targetColumns.Select(static c => (c.Name, c.FullTypeName)).ToArray());

            _messageForUserTools.DispatcherActionInstance(() =>
            {
                CompatibilitySummary = summary;
                HasCompatibilityErrors = hasErrors;
                _existingTargetColumnNames = targetNames;
                AddProgressLine(hasErrors ? "Compatibility check found blocking issues." : "Compatibility check passed.");
                UpdateStartEnabled();
            });
        }
        catch (Exception ex)
        {
            _generalApplicationData.GlobalLoggerObject.TrackError(ex, isCrash: false);
            CompatibilitySummary = $"Compatibility check failed: {ex.Message}";
            HasCompatibilityErrors = true;
            _existingTargetColumnNames = null;
            UpdateStartEnabled();
        }
        finally
        {
            IsCheckingCompatibility = false;
        }
    }

    /// <summary>
    /// Pure source-vs-target compatibility audit. Returns the report text, the ordered target
    /// column names aligned 1:1 with the source columns (null on blocking errors), and whether
    /// blocking errors exist.
    /// </summary>
    public static (string Summary, string[]? TargetColumnNames, bool HasErrors) BuildCompatibilityReport(
        IReadOnlyList<string> sourceHeaders,
        IReadOnlyList<ImportColumnKind> sourceKinds,
        IReadOnlyList<(string Name, string FullTypeName)> targetColumns)
    {
        var sb = new StringBuilder();
        var targetByUpper = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, type) in targetColumns)
        {
            targetByUpper[name] = type;
        }

        var mapping = new string[sourceHeaders.Count];
        var errors = new List<string>();
        var warnings = new List<string>();

        for (int i = 0; i < sourceHeaders.Count; i++)
        {
            string sourceName = sourceHeaders[i];
            ImportColumnKind kind = i < sourceKinds.Count ? sourceKinds[i] : ImportColumnKind.Nvarchar;

            if (!targetByUpper.TryGetValue(sourceName, out string? targetType))
            {
                errors.Add($"Source column '{sourceName}' has no matching column in the target table.");
                mapping[i] = string.Empty;
                continue;
            }

            if (!IsTypeCompatible(kind, targetType))
            {
                errors.Add($"Type conflict for '{sourceName}': source {kind} vs target {targetType}.");
                mapping[i] = string.Empty;
                continue;
            }

            mapping[i] = sourceName;
        }

        sb.AppendLine(errors.Count == 0 ? "Compatibility OK." : $"{errors.Count} blocking issue(s):");
        foreach (string error in errors)
        {
            sb.AppendLine("  " + error);
        }

        if (targetByUpper.Count > sourceHeaders.Count && errors.Count == 0)
        {
            warnings.Add($"Target has {targetByUpper.Count - sourceHeaders.Count} extra column(s); they will keep their defaults.");
        }

        foreach (string warning in warnings)
        {
            sb.AppendLine("  ! " + warning);
        }

        string[]? result = errors.Count == 0 ? mapping : null;
        return (sb.ToString().TrimEnd(), result, errors.Count > 0);
    }

    public static bool IsTypeCompatible(ImportColumnKind source, string targetFullType)
    {
        string t = targetFullType.ToUpperInvariant();
        return source switch
        {
            ImportColumnKind.Integer => t.Contains("INT", StringComparison.Ordinal),
            ImportColumnKind.Numeric => t.Contains("NUMERIC", StringComparison.Ordinal)
                || t.Contains("DECIMAL", StringComparison.Ordinal)
                || t.Contains("NUMBER", StringComparison.Ordinal)
                || t.Contains("FLOAT", StringComparison.Ordinal)
                || t.Contains("DOUBLE", StringComparison.Ordinal)
                || t.Contains("REAL", StringComparison.Ordinal),
            ImportColumnKind.Nvarchar => t.Contains("CHAR", StringComparison.Ordinal)
                || t.Contains("VARCHAR", StringComparison.Ordinal)
                || t.Contains("TEXT", StringComparison.Ordinal)
                || t.Contains("STRING", StringComparison.Ordinal),
            ImportColumnKind.Date => t.Contains("DATE", StringComparison.Ordinal)
                && !t.Contains("TIME", StringComparison.Ordinal),
            ImportColumnKind.TimeStamp => t.Contains("TIMESTAMP", StringComparison.Ordinal)
                || t.Contains("DATETIME", StringComparison.Ordinal)
                || t.Contains("DATE", StringComparison.Ordinal),
            ImportColumnKind.Boolean => t.Contains("BOOL", StringComparison.Ordinal),
            _ => t.Contains("CHAR", StringComparison.Ordinal)
        };
    }

    #endregion

    #region Quick import (clipboard / dropped files)

    /// <summary>Opens the wizard for a quick import and runs it immediately.</summary>
    public async Task StartQuickImportAsync(string sourcePath, string? connectionName, string? database)
    {
        _isQuickImport = true;
        if (!string.IsNullOrWhiteSpace(connectionName))
        {
            var conn = ConnectionsList?.FirstOrDefault(c =>
                string.Equals(c.Name, connectionName, StringComparison.OrdinalIgnoreCase));
            if (conn is not null)
            {
                SelectedConnection = conn;
            }
        }

        if (!string.IsNullOrWhiteSpace(database))
        {
            if (!DatabaseItems.Contains(database))
            {
                DatabaseItems.Add(database);
            }

            SelectedDatabase = database;
        }

        try
        {
            await OpenMethod(sourcePath);
            if (_currentImportFromExcelFile is null)
            {
                AddProgressLine($"IMPORT FAILED to {sourcePath}");
                return;
            }

            await ImportStart("Fast");
        }
        catch (Exception ex)
        {
            _generalApplicationData.GlobalLoggerObject.LogAndShowError(ex, _messageForUserTools);
        }
    }

    #endregion

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ImportStart(string option)
    {
        if (option == "Continue")
        {
            ContinueEnabled = false;
            return;
        }

        string importFilePath = ImportFilepath;
        if (File.Exists(importFilePath))
        {
            if (!_importFromExcelFilesClasses.ContainsKey(importFilePath))
            {
                await OpenMethod(importFilePath);
            }
            if (!_importFromExcelFilesClasses.TryGetValue(importFilePath, out var curentImportFromFile))
                return;

            if (SelectedConnectionTyped is null)
            {
                _messageForUserTools.ShowSimpleMessageBoxInstance("Select a connection first");
                return;
            }

            if (DestinationMode == ImportDestinationMode.Existing
                && (string.IsNullOrWhiteSpace(SelectedTableText) || SelectedTableText == _createNewTxt))
            {
                ValidationSummary = "Select an existing destination table.";
                ValidationErrorCount = 1;
                return;
            }

            ApplySelectedSheetsToImport(curentImportFromFile);
            if (curentImportFromFile.SheetNamesToImport.Count == 0)
            {
                ValidationSummary = "Select at least one sheet to import.";
                ValidationErrorCount = 1;
                return;
            }

            IsValidating = true;
            IReadOnlyList<ImportValidationError> validationErrors;
            try
            {
                validationErrors = await curentImportFromFile.ValidateSelectedSheetsAsync();
            }
            finally
            {
                IsValidating = false;
            }

            if (validationErrors.Count > 0)
            {
                ValidationErrorCount = validationErrors.Count;
                ValidationSummary = $"Import blocked: {validationErrors.Count:N0} invalid value(s). {validationErrors[0]}";
                if (SelectedTab is not null && curentImportFromFile.GetTypeChooser(SelectedTab.TabName) is { } chooser)
                    ApplyValidationErrors(chooser, validationErrors);
                UpdateStartEnabled();
                return;
            }

            if (DestinationMode == ImportDestinationMode.Existing && _existingTargetColumnNames is null)
            {
                ValidationSummary = "Run the compatibility check for the existing table first.";
                ValidationErrorCount = 1;
                return;
            }

            if (SelectedTableText == _createNewTxt || string.IsNullOrWhiteSpace(SelectedTableText))
            {
                string nme = StringExtension.RandomSuffix("IMPORTED_");
                if (TableItems.Count >= 2)
                {
                    TableItems.Insert(1, nme);
                }
                else
                {
                    TableItems.Add(nme);
                }

                SelectedTableText = nme;
            }

            IDatabaseService service = await Task.Run(() =>
                _databaseServiceResolver.GetDatabaseService(_generalApplicationData, SelectedConnectionTyped.Name));

            string tableNameMask = SelectedTableText?.Trim();
            if (!string.IsNullOrEmpty(tableNameMask))
            {
                if ((tableNameMask.StartsWith("\"") && tableNameMask.EndsWith("\""))
                    || (tableNameMask.StartsWith("'") && tableNameMask.EndsWith("'")))
                {
                    tableNameMask = tableNameMask[1..^1];
                }

                int lastDot = tableNameMask.LastIndexOf('.');
                if (lastDot >= 0 && lastDot < tableNameMask.Length - 1)
                {
                    tableNameMask = tableNameMask[(lastDot + 1)..];
                }
            }

            List<string> sheets = curentImportFromFile.SheetNamesToImport.ToList();
            var usingOptions = BuildUsingOptions();
            ImportUsingOptionsContext.Current = usingOptions;
            ImportTargetContext.TargetColumnNames = DestinationMode == ImportDestinationMode.Existing
                ? _existingTargetColumnNames
                : null;

            // For Netezza the import helper builds the fully-qualified name itself.
            string schemaForImport = service.DatabaseType == DatabaseTypeEnum.NetezzaSQL ? null : SelectedSchema;

            ResetProgress();
            AddProgressLine($"Import started: {sheets.Count:N0} sheet(s) -> {SelectedDatabase}.{SelectedSchema}.{tableNameMask}");
            curentImportFromFile.StandardMessageAction = AddProgressLine;

            try
            {
                _importActive = true;
                IsImportRunning = true;
                StartEnabled = false;
                if (option == "Fast")
                {
                    await curentImportFromFile.ImportFromFileAllSteps(service.DatabaseType, service, schemaForImport, tableNameMask);
                }
                else if (option == "WithSteps")
                {
                    var importTaskWithSteps = curentImportFromFile.ImportFromFileStepByStep(service.DatabaseType, service, schemaForImport, tableNameMask);
                    await foreach (var item in importTaskWithSteps)
                    {
                        ContinueEnabled = true;
                        StartEnabled = false;
                        var func = item?.Func;
                        var imp = item?.ImportJob;

                        SelIndexOpt = 1;
                        PopulateColumnsFromJob(imp as DbImportJob);
                        while (ContinueEnabled)
                        {
                            await Task.Delay(50);
                        }
                        await func?.Invoke();
                        SelIndexOpt = 0;
                    }
                }
                else
                {
                    _messageForUserTools.ShowSimpleMessageBoxInstance("wrong option");
                }

                if (sheets.Count > 1)
                {
                    StringBuilder sb = new();
                    foreach (var item in sheets)
                    {
                        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"{SelectedDatabase}.{SelectedSchema}.{tableNameMask}_{item}");
                    }

                    TabsWarningMessage = sb.ToString();
                }
                else
                {
                    TabsWarningMessage = $"{SelectedDatabase}.{SelectedSchema}.{tableNameMask}";
                }

                var resultHeaders = SelectedTab is not null
                    ? curentImportFromFile.GetTypeChooser(SelectedTab.TabName)?.NormalizedColumnHeaderNames
                    : null;
                SetCompleted(tableNameMask, resultHeaders);
                AddProgressLine($"FINISHED ** {tableNameMask} **");
                ActiveDocumentManager.InsertTextToActiveDocument($"SELECT * FROM {tableNameMask};\n", true);
            }
            catch (ImportValidationException ex)
            {
                ValidationErrorCount = ex.Errors.Count;
                ValidationSummary = $"Import blocked: {ex.Errors.Count:N0} invalid value(s). {ex.Errors[0]}";
                curentImportFromFile.DoFileDispose();
                _generalApplicationData.GlobalLoggerObject.LogAndShowError(ex, _messageForUserTools);
                return;
            }
            catch (Exception ex)
            {
                TabsWarningMessage = ex.Message;
                curentImportFromFile?.DoFileDispose();
                _generalApplicationData.GlobalLoggerObject.LogAndShowError(ex, _messageForUserTools);
                AddProgressLine($"FAILED: {ex.Message}");
                return;
            }
            finally
            {
                _importActive = false;
                IsImportRunning = false;
                _currentImportFromExcelFile = null;
                _importFromExcelFilesClasses.Remove(importFilePath);
                ContinueEnabled = false;
                ImportUsingOptionsContext.Current = null;
                ImportTargetContext.TargetColumnNames = null;
                IsValidating = false;
                IsDetecting = false;
                UpdateStartEnabled();
            }
        }
    }

    public ObservableCollection<ConnectionItem> ConnectionsList => SqlDocumentViewModelHelper.ConnectionsList;
}

public sealed partial class TabItem : ObservableObject
{
    public Action<TabItem>? TabOkChanged { get; set; }

    [ObservableProperty]
    public partial string TabName { get; set; }

    [ObservableProperty]
    public partial bool TabOk { get; set; }

    partial void OnTabOkChanged(bool value)
    {
        if (value)
        {
            TabOkChanged?.Invoke(this);
        }
    }
}

public sealed partial class ColumnInGrid : ObservableObject
{
    private readonly DbTypeWithSize[] _target;
    private readonly int _index;
    private readonly DbTypeWithSize _detected;
    private readonly int _detectedTextLength;
    private readonly Func<DbTypeWithSize, string>? _typeFormatter;
    private readonly Action? _typeChanged;
    private bool _legacyMutableDetectedType;

    public ColumnInGrid(string columnName, string detectedType, DbTypeWithSize[] target, int index, Func<DbTypeWithSize, string>? typeFormatter = null)
        : this(columnName, GetTypeAt(target, index), target, index, 0, typeFormatter)
    {
        DetectedType = detectedType;
        _legacyMutableDetectedType = true;
    }

    public ColumnInGrid(string columnName, DbTypeWithSize detected, DbTypeWithSize[] target, int index,
        int detectedTextLength = 0, Func<DbTypeWithSize, string>? typeFormatter = null, Action? typeChanged = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (index < 0 || index >= target.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        ColumnName = columnName;
        _target = target;
        _index = index;
        _detected = detected;
        _detectedTextLength = detectedTextLength;
        _typeFormatter = typeFormatter;
        _typeChanged = typeChanged;
        DetectedType = FormatApplied(_detected);
        SelectedChoice = ChoiceFor(target[index].DatabaseTypeSimple);
        SelectedType = FormatApplied(target[index]);
        IsOverridden = target[index].DatabaseTypeSimple != detected.DatabaseTypeSimple;
        ResetToDetectedCommand = new RelayCommand(ResetToDetected);
    }

    [ObservableProperty]
    public partial string ColumnName { get; set; }

    [ObservableProperty]
    public partial string DetectedType { get; set; }

    [ObservableProperty]
    public partial string SelectedType { get; set; }

    [ObservableProperty]
    public partial bool IsOverridden { get; set; }

    [ObservableProperty]
    public partial string ValidationError { get; set; } = "";

    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationError);

    public IReadOnlyList<TypeChoice> AvailableChoices => TypeChoice.All;

    [ObservableProperty]
    public partial TypeChoice SelectedChoice { get; set; }

    public ICommand ResetToDetectedCommand { get; }

    partial void OnSelectedChoiceChanged(TypeChoice value)
    {
        ApplySelectedType();
    }

    private static TypeChoice ChoiceFor(DbSimpleType type)
        => TypeChoice.All.FirstOrDefault(c => c.Value == type)
           ?? TypeChoice.All.First(c => c.Value == DbSimpleType.Nvarchar);

    private string FormatApplied(DbTypeWithSize type)
        => _typeFormatter?.Invoke(type) ?? type.ToString();

    private void ApplySelectedType()
    {
        if (SelectedChoice is null)
        {
            return;
        }

        DbTypeWithSize next = SelectedChoice.Value == _detected.DatabaseTypeSimple
            ? _detected
            : TypeChoice.ToDbTypeWithSize(SelectedChoice.Value, _detected, _detectedTextLength);

        if (next.Equals(_target[_index]))
        {
            return;
        }

        _target[_index] = next;
        SelectedType = FormatApplied(next);
        if (_legacyMutableDetectedType)
            DetectedType = SelectedType;
        IsOverridden = SelectedChoice.Value != _detected.DatabaseTypeSimple;
        _typeChanged?.Invoke();
    }

    public void ResetToDetected()
    {
        SelectedChoice = ChoiceFor(_detected.DatabaseTypeSimple);
        _target[_index] = _detected;
        SelectedType = FormatApplied(_detected);
        if (_legacyMutableDetectedType)
            DetectedType = SelectedType;
        IsOverridden = false;
        _typeChanged?.Invoke();
    }

    /// <summary>Bulk-action entry: applies the given type (used by "all as text" etc.).</summary>
    public void ApplyType(DbSimpleType type)
    {
        DbTypeWithSize next = type == _detected.DatabaseTypeSimple
            ? _detected
            : TypeChoice.ToDbTypeWithSize(type, _detected, _detectedTextLength);
        if (next.Equals(_target[_index]))
        {
            return;
        }

        _target[_index] = next;
        SelectedChoice = ChoiceFor(type);
        SelectedType = FormatApplied(next);
        if (_legacyMutableDetectedType)
            DetectedType = SelectedType;
        IsOverridden = type != _detected.DatabaseTypeSimple;
        _typeChanged?.Invoke();
    }

    partial void OnValidationErrorChanged(string value) => OnPropertyChanged(nameof(HasValidationError));

    private static DbTypeWithSize GetTypeAt(DbTypeWithSize[] target, int index)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (index < 0 || index >= target.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        return target[index];
    }
}

public sealed class TypeChoice
{
    public static IReadOnlyList<TypeChoice> All { get; } =
    [
        new(DbSimpleType.Integer, "BIGINT"),
        new(DbSimpleType.Numeric, "NUMERIC"),
        new(DbSimpleType.Nvarchar, "NVARCHAR (text)"),
        new(DbSimpleType.Date, "DATE"),
        new(DbSimpleType.TimeStamp, "TIMESTAMP"),
        new(DbSimpleType.Boolean, "BOOLEAN"),
    ];

    public DbSimpleType Value { get; }
    public string Label { get; }

    public TypeChoice(DbSimpleType value, string label)
    {
        Value = value;
        Label = label;
    }

    public static DbTypeWithSize ToDbTypeWithSize(DbSimpleType type, DbTypeWithSize original, int detectedTextLength = 0)
    {
        return type switch
        {
            DbSimpleType.Nvarchar => new DbTypeWithSize(DbSimpleType.Nvarchar)
            {
                TextLength = original.DatabaseTypeSimple == DbSimpleType.Nvarchar && original.TextLength > 0
                    ? original.TextLength
                    : Math.Max(DatabaseTypeChooser.DEFAULT_NVARCHAR_LENGTH, detectedTextLength)
            },
            DbSimpleType.Numeric => new DbTypeWithSize(DbSimpleType.Numeric)
            {
                NumericPrecision = original.DatabaseTypeSimple == DbSimpleType.Numeric && original.NumericPrecision > 0
                    ? original.NumericPrecision
                    : 20,
                NumericScale = original.DatabaseTypeSimple == DbSimpleType.Numeric && original.NumericScale > 0
                    ? original.NumericScale
                    : 6
            },
            DbSimpleType.Integer => new DbTypeWithSize(DbSimpleType.Integer),
            DbSimpleType.Date => new DbTypeWithSize(DbSimpleType.Date),
            DbSimpleType.TimeStamp => new DbTypeWithSize(DbSimpleType.TimeStamp),
            DbSimpleType.Boolean => new DbTypeWithSize(DbSimpleType.Boolean),
            _ => new DbTypeWithSize(DbSimpleType.Nvarchar) { TextLength = DatabaseTypeChooser.DEFAULT_NVARCHAR_LENGTH }
        };
    }

    public override string ToString() => Label;
}
