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
using JustyBase.Services.Documents;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;

namespace JustyBase.ViewModels.Documents;

public sealed partial class ImportViewModel
{
    private readonly IGeneralApplicationData _generalApplicationData;
    private readonly IMessageForUserTools _messageForUserTools;
    private readonly IDatabaseServiceResolver _databaseServiceResolver;

    public ObservableCollection<ColumnInGrid> ColumnsInGrid { get; set; } = [];
    public ObservableCollection<string[]> PreviewRows { get; set; } = [];

    private readonly Dictionary<string, ImportFromExcelFile> _importFromExcelFilesClasses = [];
    public ICommand OpenFileForImportCommand { get; set; }
    public ObservableCollection<TabItem> ExcelTabsNames { get; set; } = [];
    public ObservableCollection<ConnectionItem> ConnectionsList => SqlDocumentViewModelHelper.ConnectionsList;

    [ObservableProperty]
    public partial TabItem SelectedTab { get; set; }

    private readonly string _createNewTxt = "[CREATE NEW TABLE]";

    [ObservableProperty]
    public partial bool AllColumnsAsText { get; set; }

    private readonly ConcurrentBag<ImportItem> _importsInProgress = [];

    [ObservableProperty]
    public partial string TabsWarningMessage { get; set; }

    [ObservableProperty]
    public partial bool StartEnabled { get; set; }

    [ObservableProperty]
    public partial string ImportFilepath { get; set; }
    [ObservableProperty]
    public partial bool ContinueEnabled { get; set; }

    /// <summary>EXTERNAL USING column delimiter (default tab). Applied on the Netezza pipe path.</summary>
    [ObservableProperty]
    public partial string UsingDelimiter { get; set; } = "\\t";

    /// <summary>Source-file and EXTERNAL USING encoding name.</summary>
    [ObservableProperty]
    public partial string UsingEncoding { get; set; } = "utf-8";

    /// <summary>Optional EXTERNAL USING MAXROWS; empty or &lt;= 0 omits the clause.</summary>
    [ObservableProperty]
    public partial string UsingMaxRows { get; set; } = "";

    private int _databaseListLoadGeneration;
    private int _schemaListLoadGeneration;
    private int _tableListLoadGeneration;

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

    private async Task LoadDatabasesForSelectedConnectionAsync(int generation, string connectionName)
    {
        if (string.IsNullOrWhiteSpace(connectionName))
        {
            return;
        }

        List<string> databases;
        try
        {
            databases = await Task.Run(() =>
            {
                var service = _databaseServiceResolver.GetDatabaseService(_generalApplicationData, connectionName);
                return service?.GetDatabases("").ToList() ?? [];
            });
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

    public ObservableCollection<string> DatabaseItems { get; set; }
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

    private async Task LoadSchemasForSelectedDatabaseAsync(int generation, string connectionName, string database)
    {
        if (string.IsNullOrWhiteSpace(connectionName) || string.IsNullOrWhiteSpace(database))
        {
            return;
        }

        List<string> schemas;
        try
        {
            schemas = await Task.Run(() =>
            {
                var service = _databaseServiceResolver.GetDatabaseService(_generalApplicationData, connectionName);
                return service?.GetSchemas(database, "").ToList() ?? [];
            });
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

    public ObservableCollection<string> SchemaItems { get; set; }

    public string SelectedSchema
    {
        get;
        set
        {
            SetProperty(ref field, value);
            TableItems.Clear();
            TableItems.Add(_createNewTxt);
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

    private async Task LoadTablesForSelectedSchemaAsync(int generation, string connectionName, string database, string schema)
    {
        if (string.IsNullOrWhiteSpace(connectionName)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(schema))
        {
            return;
        }

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
    public ObservableCollection<string> TableItems { get; set; }

    public ObservableCollection<ImportItem> ImportItemCollections { get; set; }

    [ObservableProperty]
    public partial string SelectedTableText { get; set; }

    /// <summary>
    /// Pre-fills destination controls when opening Import from the schema explorer.
    /// </summary>
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

        var curentImportFromFile = new ImportFromExcelFile(x => _messageForUserTools.ShowSimpleMessageBoxInstance(x), _generalApplicationData.GlobalLoggerObject)
        {
            FilePath = filePath,
            TreatAllColumnsAsText = this.AllColumnsAsText
        };
        _importFromExcelFilesClasses[filePath] = curentImportFromFile;

        if (!string.IsNullOrWhiteSpace(curentImportFromFile.FilePath))
        {
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
                return;
            }

            ExcelTabsNames.Clear();
            for (int i = 0; i < curentImportFromFile.SheetNamesToImport.Count; i++)
            {
                string item = curentImportFromFile.SheetNamesToImport[i];
                ExcelTabsNames.Add(new TabItem() { TabName = item, TabOk = (i == 0) });
                SelectedTab = ExcelTabsNames[0];
            }
            TabsWarningMessage = "";
        }
    }

    [ObservableProperty]
    public partial int SelIndexOpt { get; set; }

    public Action<string[]> ActionFromView { get; set; }
    private readonly Lock _lock = new();

    [RelayCommand(AllowConcurrentExecutions = true)]
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
            var curentImportFromFile = _importFromExcelFilesClasses[importFilePath];
            //StartEnabled = false;

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

            var importItem = new ImportItem(_messageForUserTools)
            {
                SourcePath = Path.GetDirectoryName(importFilePath),
                SourceName = Path.GetFileName(importFilePath),
                StartTime = DateTime.Now,
                Elapsed = "",
                Estimated = " - ",
                Info = "started",
                Connection = SelectedConnectionTyped?.Name,
                Destination = $"{SelectedDatabase}.{SelectedSchema}.{SelectedTableText}"
            };
            lock (_lock)
            {
                _importsInProgress.Add(importItem);
            }

            ImportItemCollections.Insert(0, importItem);
#if AVALONIA
            ImportItems.Refresh();
#endif
            await Task.Delay(20);
            importItem.Bck = "Yellow";
            if (SelectedConnectionTyped is null)
            {
                _messageForUserTools.ShowSimpleMessageBoxInstance("Select a connection first");
                return;
            }

            IDatabaseService service = await Task.Run(() =>
                _databaseServiceResolver.GetDatabaseService(_generalApplicationData, SelectedConnectionTyped.Name));

            if (!_dispatcherTimer.IsEnabled)
            {
                _dispatcherTimer.Start();
            }
            if (curentImportFromFile is null)
            {
                _messageForUserTools.ShowSimpleMessageBoxInstance("_currentImport is null");
                return;
            }

            curentImportFromFile.StandardMessageAction = (s) => _messageForUserTools.DispatcherActionInstance(
                () =>
                {
                    importItem.Info = s;
                }
            );

            List<string> excelNames = [];
            foreach (var item in ExcelTabsNames)
            {
                if (item.TabOk)
                {
                    excelNames.Add(item.TabName);
                }
            }

            for (int i = 0; i < curentImportFromFile.SheetNamesToImport.Count; i++)
            {
                var item = curentImportFromFile.SheetNamesToImport[i];
                if (!excelNames.Contains(item))
                {
                    curentImportFromFile.SheetNamesToImport.Remove(item);
                }
            }

            string tableNameMask = SelectedTableText;
            var sheets = curentImportFromFile.SheetNamesToImport;
            var usingOptions = BuildUsingOptions();
            ImportUsingOptionsContext.Current = usingOptions;

            try
            {
                if (option == "Fast")
                {
                    var fastImportTask = curentImportFromFile.ImportFromFileAllSteps(service.DatabaseType, service, SelectedSchema, tableNameMask);
                    _importFromExcelFilesClasses.Remove(importFilePath);
                    StartEnabled = true;
                    await fastImportTask;
                }
                else if (option == "WithSteps")
                {
                    ColumnsInGrid.Clear();
                    var importTaskWithSteps = curentImportFromFile.ImportFromFileStepByStep(service.DatabaseType, service, SelectedSchema, tableNameMask,
                        (x, y) =>
                        {
                            ColumnsInGrid.Add(new ColumnInGrid()
                            {
                                ColumnName = x,
                                DetectedType = y,
                                DoForceText = false,
                            });
                            OnPropertyChanged(nameof(ColumnsInGrid));
                        }
                            , x =>
                            {
                                x.ForEach(x => PreviewRows.Add(x));
                                _messageForUserTools.DispatcherActionInstance(() => ActionFromView(ColumnsInGrid.Select(o => o.ColumnName).ToArray()));
                            }
                        );
                    _importFromExcelFilesClasses.Remove(importFilePath);
                    await foreach (var item in importTaskWithSteps)
                    {
                        ContinueEnabled = true;
                        StartEnabled = false;
                        var func = item?.Func;
                        var imp = item?.ImportJob;

                        SelIndexOpt = 1;
                        while (ContinueEnabled)
                        {
                            await Task.Delay(50);
                        }
                        for (int l = 0; l < ColumnsInGrid.Count; l++)
                        {
                            if (ColumnsInGrid[l].DoForceText)
                            {
                                imp.ColumnTypesBestMatch[l] = new DbTypeWithSize(DbSimpleType.Nvarchar) { TextLength = DatabaseTypeChooser.DEFAULT_NVARCHAR_LENGTH };
                            }
                        }
                        await func?.Invoke();
                        SelIndexOpt = 0;
                    }
                    StartEnabled = true;
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

                importItem.Info = "completed !";
                importItem.Elapsed = (DateTime.Now - importItem.StartTime).ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.CurrentCulture);
                importItem.Bck = "LightGreen";
#if AVALONIA
                ImportItems.Refresh();
#endif
                lock (_lock)
                {
                    _importsInProgress.TryTake(out importItem);
                    _dispatcherTimer.Stop();
                }
            }
            catch (Exception ex)
            {
                TabsWarningMessage = ex.Message;
                curentImportFromFile?.DoFileDispose();
                _generalApplicationData.GlobalLoggerObject.LogAndShowError(ex, _messageForUserTools);
                return;
            }
            finally
            {
                ImportUsingOptionsContext.Current = null;
            }
        }
    }
}

public sealed partial class ImportItem : ObservableObject
{
    [ObservableProperty]
    public partial string Info { get; set; }
    public string SourceName { get; set; }
    public string SourcePath { get; set; }
    public string Connection { get; set; }
    public string Destination { get; set; }
    public DateTime StartTime { get; set; }

    [ObservableProperty]
    public partial string Elapsed { get; set; }

    [ObservableProperty]
    public partial string Estimated { get; set; }

    [ObservableProperty]
    public partial string Bck { get; set; }
    public ICommand StopCommand { get; set; }
    public ImportItem(IMessageForUserTools messageForUserTools)
    {
        StopCommand = new RelayCommand(() =>
        {
            messageForUserTools.ShowSimpleMessageBoxInstance($"to do {Info} {StartTime}");
        });
        Bck = "Transparent";
    }
}

public sealed partial class TabItem : ObservableObject
{
    [ObservableProperty]
    public partial string TabName { get; set; }

    [ObservableProperty]
    public partial bool TabOk { get; set; }
}

public sealed partial class ColumnInGrid : ObservableObject
{
    [ObservableProperty]
    public partial string ColumnName { get; set; }

    [ObservableProperty]
    public partial string DetectedType { get; set; }

    public bool DoForceText
    {
        get;
        set
        {
            if (value)
            {
                DetectedType = $"NVARCHAR({DatabaseTypeChooser.DEFAULT_NVARCHAR_LENGTH})";
            }
            SetProperty(ref field, value);
        }
    }
}
