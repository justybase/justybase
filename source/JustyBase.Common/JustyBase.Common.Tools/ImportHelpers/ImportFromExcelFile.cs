using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using JustyBase.PluginCommon.Models;
using JustyBase.PluginCommons;
using SpreadSheetTasks;
using System.Runtime.ExceptionServices;
using System.Text;

namespace JustyBase.Common.Tools.ImportHelpers;

public sealed class ImportFromExcelFile(Action<string>? exceptionMessageAction, ISimpleLogger? logger)
{
    private readonly Action<string>? _exceptionMessageAction = exceptionMessageAction;
    private readonly ISimpleLogger? _logger = logger;

    public Action<string>? StandardMessageAction { get; set; }

    public List<string>? SheetNamesToImport { get; set; }

    private ExcelReaderAbstract _excelReader = null!;
    public ExcelReaderAbstract ExcelReader => _excelReader;

    private readonly Dictionary<string, DatabaseTypeChooser> _typeChoosers = [];
    private readonly object _typeCacheLock = new();
    private readonly SemaphoreSlim _detectionGate = new(1, 1);
    private int _typeCacheGeneration;
    private Encoding? _sourceEncoding;

    /// <summary>Returns the cached type detection for a sheet, if already analysed.</summary>
    public DatabaseTypeChooser? GetTypeChooser(string sheetName)
    {
        lock (_typeCacheLock)
        {
            return _typeChoosers.TryGetValue(sheetName, out var chooser) ? chooser : null;
        }
    }

    /// <summary>Drops all cached type detection so the next detection/import re-scans the source.</summary>
    public void InvalidateTypeCache()
    {
        Interlocked.Increment(ref _typeCacheGeneration);
        lock (_typeCacheLock)
        {
            _typeChoosers.Clear();
        }
    }

    /// <summary>
    /// initialize and read tab names
    /// purpouse of spliting loginc with InitImport + rest of codwe is to allow user to change data types and select specific excel sheet.
    /// </summary>
    /// <param name="filePath"></param>
    /// <param name="encoding"></param>
    public bool InitImport(Encoding? encoding = null)
    {
        string filePath = FilePath ?? throw new InvalidOperationException("An import file path must be configured before initialization.");
        _sourceEncoding = encoding;
        if (filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || filePath.EndsWith(".xlsb", StringComparison.OrdinalIgnoreCase))
        {
            _excelReader = new XlsxOrXlsbReadOrEdit();
        }
        else
        {
            CompressionEnum compression = filePath.GetCsvCompressionEnum();
            if (compression == CompressionEnum.None)
            {
                _excelReader = new CsvReader();
            }
            else
            {
                _excelReader = new CsvReader(compression);
            }
        }

        try
        {
            _excelReader.Open(filePath, true, encoding: encoding);
            SheetNamesToImport = _excelReader.GetSheetNames().ToList();
            return true;
        }
        catch (Exception ex)
        {
            _logger?.TrackError(ex, isCrash: false);

            DoFileDispose();
            return false;
        }
    }

    public void DoFileDispose()
    {
        try
        {
            _excelReader?.Dispose();
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Runs (or returns the cached result of) the type scan for a single sheet.
    /// Used by the UI to let the user review/override column types before the import starts.
    /// Scans are serialized per file instance (the underlying reader is not thread-safe), and
    /// results are only cached if the cache was not invalidated while the scan was running.
    /// </summary>
    public async Task<DatabaseTypeChooser?> DetectSheetAsync(
        string sheetName,
        Action<string>? messageAction = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(FilePath) || _excelReader is null)
        {
            return null;
        }

        lock (_typeCacheLock)
        {
            if (_typeChoosers.TryGetValue(sheetName, out var cached))
            {
                return cached;
            }
        }

        await _detectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_typeCacheLock)
            {
                if (_typeChoosers.TryGetValue(sheetName, out var cached))
                {
                    return cached;
                }
            }

            int generationAtStart = Volatile.Read(ref _typeCacheGeneration);
            var chooser = await Task.Run(() =>
            {
                try
                {
                    _excelReader.TreatAllColumnsAsText = TreatAllColumnsAsText;
                    _excelReader.ActualSheetName = sheetName;
                    var c = new DatabaseTypeChooser();
                    c.ExcelTypeDetection(_excelReader, sheetName, messageAction, (long)TimeSpan.FromHours(4).TotalSeconds);
                    cancellationToken.ThrowIfCancellationRequested();
                    return c;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger?.TrackError(ex, isCrash: false);
                    _exceptionMessageAction?.Invoke(ex.Message);
                    return null;
                }
            }).ConfigureAwait(false);

            if (chooser is not null && generationAtStart == Volatile.Read(ref _typeCacheGeneration))
            {
                lock (_typeCacheLock)
                {
                    _typeChoosers[sheetName] = chooser;
                }
            }

            return chooser;
        }
        finally
        {
            _detectionGate.Release();
        }
    }

    public string? FilePath { get; set; }

    private bool _treatAllColumnsAsText;
    public bool TreatAllColumnsAsText
    {
        get => _treatAllColumnsAsText;
        set
        {
            if (_treatAllColumnsAsText != value)
            {
                _treatAllColumnsAsText = value;
                InvalidateTypeCache();
            }
        }
    }

    /// <summary>
    /// Validates every selected sheet with the exact reader and selected type plan that will be used by import.
    /// The source is reopened afterwards, so a successful validation never consumes the subsequent import.
    /// </summary>
    public async Task<IReadOnlyList<ImportValidationError>> ValidateSelectedSheetsAsync(
        IEnumerable<string>? sheetNames = null,
        CancellationToken cancellationToken = default)
    {
        string[] selected = (sheetNames ?? SheetNamesToImport ?? []).Distinct(StringComparer.Ordinal).ToArray();
        if (selected.Length == 0 || _excelReader is null)
            return [];

        foreach (string sheet in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetTypeChooser(sheet) is null && await DetectSheetAsync(sheet, cancellationToken: cancellationToken) is null)
                return [new ImportValidationError(sheet, 0, 0, string.Empty,
                    new DbTypeWithSize(DbSimpleType.Nvarchar), string.Empty,
                    "The sheet could not be analysed.")];
            GetTypeChooser(sheet)?.SetValidationErrors([]);
        }

        List<ImportValidationError> errors;
        if (IsXlsbSource())
        {
            // XlsbReader opens the source with an exclusive FileStream. A second reader cannot
            // be opened while the UI's reader is alive, so validate with the existing reader,
            // then dispose it before reopening it for the import job.
            await _detectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                List<ImportValidationError>? validatedErrors = null;
                ExceptionDispatchInfo? validationException = null;
                Exception? reopenException = null;
                try
                {
                    validatedErrors = await Task.Run(
                        () => ValidateWithReader(_excelReader, selected, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    validationException = ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    // XLSB keeps an exclusive stream open. The reader must be closed and
                    // reopened while the gate is still held, including when validation fails.
                    DoFileDispose();
                    try
                    {
                        if (!InitImport(_sourceEncoding))
                            reopenException = new IOException("The import source could not be reopened after validation.");
                        else
                            SheetNamesToImport = selected.ToList();
                    }
                    catch (Exception ex)
                    {
                        reopenException = ex;
                    }
                }

                if (reopenException is not null)
                {
                    if (validationException is not null)
                        throw new AggregateException("The XLSB reader could not be restored after validation.", validationException.SourceException, reopenException);

                    ExceptionDispatchInfo.Capture(reopenException).Throw();
                }

                validationException?.Throw();
                errors = validatedErrors!;
            }
            finally
            {
                _detectionGate.Release();
            }
        }
        else
        {
            errors = await Task.Run(() =>
            {
                ExcelReaderAbstract validationReader = CreateReaderForSource();
                try
                {
                    validationReader.Open(FilePath!, true, encoding: _sourceEncoding);
                    return ValidateWithReader(validationReader, selected, cancellationToken);
                }
                finally
                {
                    validationReader.Dispose();
                }
            }, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
        }

        cancellationToken.ThrowIfCancellationRequested();
        foreach (IGrouping<string, ImportValidationError> group in errors.GroupBy(e => e.SheetName, StringComparer.Ordinal))
        {
            GetTypeChooser(group.Key)?.SetValidationErrors(group);
        }
        return errors;
    }

    private ExcelReaderAbstract CreateReaderForSource()
    {
        string filePath = FilePath ?? throw new InvalidOperationException("An import file path must be configured before validation.");
        if (filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || filePath.EndsWith(".xlsb", StringComparison.OrdinalIgnoreCase))
            return new XlsxOrXlsbReadOrEdit();

        CompressionEnum compression = filePath.GetCsvCompressionEnum();
        return compression == CompressionEnum.None ? new CsvReader() : new CsvReader(compression);
    }

    private bool IsXlsbSource()
        => FilePath?.EndsWith(".xlsb", StringComparison.OrdinalIgnoreCase) == true;

    private List<ImportValidationError> ValidateWithReader(
        ExcelReaderAbstract validationReader,
        IReadOnlyList<string> selected,
        CancellationToken cancellationToken)
    {
        List<ImportValidationError> result = [];
        foreach (string sheet in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            validationReader.TreatAllColumnsAsText = TreatAllColumnsAsText;
            validationReader.ActualSheetName = sheet;
            if (validationReader is not CsvReader)
                validationReader.Read();

            DatabaseTypeChooser chooser = GetTypeChooser(sheet)
                ?? throw new InvalidOperationException($"No type plan is available for sheet '{sheet}'.");
            using var reader = new DataReaderFromExcelReaderAbstract(validationReader, chooser);
            int rowNumber = 1; // row one is the header
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                rowNumber++;
                for (int column = 0; column < reader.FieldCount; column++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (reader.IsDBNull(column))
                        continue;

                    try
                    {
                        _ = reader.GetValue(column);
                    }
                    catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
                    {
                        DbTypeWithSize selectedType = chooser.ColumnTypesBestMatch![column];
                        result.Add(new ImportValidationError(
                            sheet,
                            rowNumber,
                            column,
                            chooser.NormalizedColumnHeaderNames![column],
                            selectedType,
                            GetSourceValue(reader, column),
                            ex.Message));
                    }
                }
            }
        }
        return result;
    }

    private static string GetSourceValue(System.Data.IDataReader reader, int column)
    {
        try
        {
            return reader.GetString(column);
        }
        catch
        {
            return Convert.ToString(reader[column], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }
    public async IAsyncEnumerable<DbImportJob> ReadFileAndReturnSingleImportJobs()
    {
        var tabsToImport = SheetNamesToImport?.ToArray();
        if (tabsToImport == null) yield break;
        var progressMessage = StandardMessageAction;
        try
        {
            foreach (var sheetName in _excelReader.GetSheetNames().Where(x => tabsToImport.Contains(x, StringComparer.Ordinal)))
            {
                _excelReader.TreatAllColumnsAsText = TreatAllColumnsAsText;
                _excelReader.ActualSheetName = sheetName;
                DatabaseTypeChooser? databaseTypeChooser;
                lock (_typeCacheLock)
                {
                    _typeChoosers.TryGetValue(sheetName, out databaseTypeChooser);
                }
                if (databaseTypeChooser is null)
                {
                    databaseTypeChooser = new DatabaseTypeChooser();
                    StandardMessageAction?.Invoke("data scan started");
                    await _detectionGate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        await Task.Run(() => databaseTypeChooser.ExcelTypeDetection(_excelReader, _excelReader.ActualSheetName, StandardMessageAction, (long)TimeSpan.FromHours(4).TotalSeconds));
                    }
                    finally
                    {
                        _detectionGate.Release();
                    }
                    StandardMessageAction?.Invoke("data scan ended");
                    lock (_typeCacheLock)
                    {
                        _typeChoosers[sheetName] = databaseTypeChooser;
                    }
                }

                if (_excelReader is not CsvReader)//skipHeader?
                {
                    _excelReader.Read();
                }
                if (_excelReader is CsvReader csvReader)
                {
                    string path = csvReader.FilePath ?? string.Empty;
                    var compression = csvReader.Compression;
                    _excelReader.Dispose();
                    _excelReader = new CsvReader(compression)
                    {
                        TreatAllColumnsAsText = TreatAllColumnsAsText
                    };
                    _excelReader.Open(path);
                }

                yield return new DbImportJob(new DataReaderFromExcelReaderAbstract(_excelReader, databaseTypeChooser), databaseTypeChooser)
                {
                    SourceSheetName = sheetName
                };
            }
        }
        finally
        {
            _excelReader.Dispose();
        }
    }

    public async IAsyncEnumerable<ImportStepHelper> ImportFromFileStepByStep(DatabaseTypeEnum databaseTypeEnum, IDatabaseWithSpecificImportService databaseService, string schemaName, string databasaTableName,
        Action<string, string>? adColumnInfo = null, Action<List<string[]>>? previewAction = null)
    {
        IReadOnlyList<ImportValidationError> validationErrors = await ValidateSelectedSheetsAsync();
        if (validationErrors.Count > 0)
            throw new ImportValidationException(validationErrors);

        var importJobs = ReadFileAndReturnSingleImportJobs();

        ArgumentNullException.ThrowIfNull(importJobs, nameof(importJobs));
        

        int i = 0;
        await foreach (DbImportJob importJob in importJobs)
        {
            ArgumentNullException.ThrowIfNull(importJob, nameof(importJob));
            ArgumentNullException.ThrowIfNull(importJob.ColumnHeadersNames, nameof(importJob.ColumnHeadersNames));
            string tmp = i == 0 ? "" : $"_{i}";
            string name = databaseTypeEnum == DatabaseTypeEnum.Oracle || string.IsNullOrEmpty(schemaName) ? $"{databasaTableName}{tmp}" : $"{schemaName}.{databasaTableName}{tmp}";

            if (importJob.ColumnTypesBestMatch is not null)
            {
                for (int j = 0; j < importJob.ColumnTypesBestMatch.Length; j++)
                {
                    adColumnInfo?.Invoke(importJob.ColumnHeadersNames[j], importJob.ColumnTypesBestMatch[j].ToString());
                }
            }
            ArgumentNullException.ThrowIfNull(importJob.PreviewRows, nameof(importJob.PreviewRows));
            previewAction?.Invoke(importJob.PreviewRows);
            yield return new ImportStepHelper()
            {
                Func = () => databaseService.DbSpecificImportPart(importJob, $"{name}", StandardMessageAction),
                ImportJob = importJob
            };
            i++;
        }
        yield break;
    }

    public async Task ImportFromFileAllSteps(DatabaseTypeEnum databaseType, IDatabaseWithSpecificImportService databaseService, string? schemaName, string databasaTableName)
    {
        try
        {
            IReadOnlyList<ImportValidationError> validationErrors = await ValidateSelectedSheetsAsync();
            if (validationErrors.Count > 0)
                throw new ImportValidationException(validationErrors);

            var importJobs = ReadFileAndReturnSingleImportJobs();

            int i = 0;
            await foreach (var importJob in importJobs)
            {
                string tmp = i == 0 ? "" : $"_{i}";
                string name = databaseType == DatabaseTypeEnum.Oracle || string.IsNullOrEmpty(schemaName) ? $"{databasaTableName}{tmp}" : $"{schemaName}.{databasaTableName}{tmp}";
                await databaseService.DbSpecificImportPart(importJob, $"{name}", StandardMessageAction);
                i++;
            }
        }
        catch (ImportValidationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.TrackError(ex, isCrash: false);
            _exceptionMessageAction?.Invoke(ex.Message);
            _exceptionMessageAction?.Invoke(ex.StackTrace ?? "no stack trace");
        }

        return;
    }
    /// <summary>
    /// shortcut method for first Excel sheet to database (all setting default)
    /// </summary>
    /// <param name="databaseType"></param>
    /// <param name="databaseWithSpecificImportService"></param>
    /// <returns></returns>
    public async Task PerformFastImportFromFileAsync(DatabaseTypeEnum databaseType, IDatabaseWithSpecificImportService databaseWithSpecificImportService)
    {
        try
        {
            if (InitImport() && SheetNamesToImport != null && SheetNamesToImport.Count > 0)
            {
                string sheetName = SheetNamesToImport[0];
                StandardMessageAction?.Invoke("\n" + sheetName);
                SheetNamesToImport.Clear();
                SheetNamesToImport.Add(sheetName);

                string randomName = StringExtension.RandomSuffix("IMP_D_");
                await ImportFromFileAllSteps(databaseType, databaseWithSpecificImportService, null, randomName);

                StandardMessageAction?.Invoke($"FINISHED ** {randomName} **");
            }
            else
            {
                StandardMessageAction?.Invoke("\n" + "import failed");
            }
        }
        catch (Exception ex)
        {
            _logger?.TrackError(ex, isCrash: false);
            _exceptionMessageAction?.Invoke(ex.Message);
        }
    }

}

public sealed class ImportStepHelper
{
    public Func<Task>? Func { get; set; }
    public DbImportJob? ImportJob { get; set; }
}



