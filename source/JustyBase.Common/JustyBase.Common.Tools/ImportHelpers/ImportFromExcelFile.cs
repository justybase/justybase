using JustyBase.ImportExport.Import;
using JustyBase.PluginCommon.Contracts;
using JustyBase.PluginCommon.Enums;
using System.Text;

namespace JustyBase.Common.Tools.ImportHelpers;

/// <summary>
/// Thin host adapter over the shared <see cref="TabularImportScanner"/>. Owns the per-sheet
/// <see cref="DatabaseTypeChooser"/> cache (the UI override plan) and maps scan results to the
/// host type model; all file orchestration lives in the shared scanner.
/// </summary>
public sealed class ImportFromExcelFile(Action<string>? exceptionMessageAction, ISimpleLogger? logger) : IDisposable
{
    private const string ScanStarted = "data scan started";
    private const string ScanEnded = "data scan ended";

    private readonly Action<string>? _exceptionMessageAction = exceptionMessageAction;
    private readonly ISimpleLogger? _logger = logger;
    private readonly TabularImportScanner _scanner = new(new SpreadSheetSourceFactory());
    private readonly Dictionary<string, DatabaseTypeChooser> _typeChoosers = [];
    private readonly object _typeCacheLock = new();
    private readonly SemaphoreSlim _detectionGate = new(1, 1);
    private int _typeCacheGeneration;

    public Action<string>? StandardMessageAction
    {
        get => _scanner.StandardMessageAction;
        set => _scanner.StandardMessageAction = value;
    }

    public List<string>? SheetNamesToImport { get; set; }

    public string? FilePath
    {
        get => _scanner.FilePath;
        set => _scanner.FilePath = value;
    }

    public bool TreatAllColumnsAsText
    {
        get => _scanner.TreatAllColumnsAsText;
        set
        {
            if (_scanner.TreatAllColumnsAsText != value)
            {
                _scanner.TreatAllColumnsAsText = value;
                InvalidateTypeCache();
            }
        }
    }

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
        lock (_typeCacheLock)
        {
            _typeChoosers.Clear();
            Interlocked.Increment(ref _typeCacheGeneration);
        }
    }

    /// <summary>Initializes and reads the tab names.</summary>
    public bool InitImport(Encoding? encoding = null)
    {
        _scanner.SourceEncoding = encoding;
        if (!_scanner.OpenSource())
        {
            return false;
        }

        SheetNamesToImport = _scanner.SheetNames.ToList();
        return true;
    }

    public void DoFileDispose()
    {
        _scanner.DisposeSource();
    }

    public void Dispose()
    {
        DoFileDispose();
        _detectionGate.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Runs (or returns the cached result of) the type scan for a single sheet. Concurrent
    /// calls coalesce onto one detection and share the resulting chooser instance.
    /// </summary>
    public async Task<DatabaseTypeChooser?> DetectSheetAsync(
        string sheetName,
        Action<string>? messageAction = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_scanner.FilePath))
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

            // Snapshot the generation before the scan: a TreatAllColumnsAsText toggle (or any
            // other invalidation) mid-scan must not let this scan repopulate the cache with
            // results computed under the OLD setting.
            int generation = Volatile.Read(ref _typeCacheGeneration);

            SheetScanResult? scan = await _scanner.ScanSheetAsync(sheetName, messageAction, cancellationToken).ConfigureAwait(false);
            if (scan is null)
            {
                return null;
            }

            var chooser = new DatabaseTypeChooser();
            chooser.ApplyScan(scan);
            lock (_typeCacheLock)
            {
                if (generation != Volatile.Read(ref _typeCacheGeneration))
                {
                    // Stale — the cache was invalidated while this scan was running; the
                    // newer detection owns the cache (and this result is not cached).
                    return null;
                }

                _typeChoosers[sheetName] = chooser;
            }

            messageAction?.Invoke("--" + string.Join('|', chooser.ColumnTypesBestMatch!.ToList()));
            return chooser;
        }
        finally
        {
            _detectionGate.Release();
        }
    }

    /// <summary>
    /// Validates every selected sheet with the exact reader and selected type plan that will
    /// be used by import. The source is reopened afterwards, so a successful validation never
    /// consumes the subsequent import.
    /// </summary>
    public async Task<IReadOnlyList<ImportValidationError>> ValidateSelectedSheetsAsync(
        IEnumerable<string>? sheetNames = null,
        CancellationToken cancellationToken = default)
    {
        string[] selected = (sheetNames ?? SheetNamesToImport ?? []).Distinct(StringComparer.Ordinal).ToArray();
        if (selected.Length == 0)
        {
            return [];
        }

        foreach (string sheet in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetTypeChooser(sheet) is null)
            {
                return [new ImportValidationError(sheet, 0, 0, string.Empty, ImportColumnKind.Nvarchar, string.Empty,
                    "The sheet could not be analysed.")];
            }

            GetTypeChooser(sheet)?.SetValidationErrors([]);
        }

        IReadOnlyList<ImportValidationError> errors = await _scanner.ValidateSelectedSheetsAsync(selected, SheetPlanFor, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        foreach (IGrouping<string, ImportValidationError> group in errors.GroupBy(e => e.SheetName, StringComparer.Ordinal))
        {
            GetTypeChooser(group.Key)?.SetValidationErrors(group);
        }

        return errors;
    }

    private SheetPlan SheetPlanFor(string sheet)
    {
        DatabaseTypeChooser chooser = GetTypeChooser(sheet)
            ?? throw new InvalidOperationException($"No type plan is available for sheet '{sheet}'.");
        return new SheetPlan(
            sheet,
            DatabaseTypeChooser.ToImportColumns(chooser.NormalizedColumnHeaderNames ?? [], chooser.ColumnTypesBestMatch ?? []),
            chooser.PreviewRows,
            chooser.RowsCount);
    }

    public async IAsyncEnumerable<DbImportJob> ReadFileAndReturnSingleImportJobs()
    {
        var tabsToImport = SheetNamesToImport?.ToArray();
        if (tabsToImport is null || tabsToImport.Length == 0)
        {
            yield break;
        }

        foreach (string sheetName in tabsToImport)
        {
            if (GetTypeChooser(sheetName) is null)
            {
                StandardMessageAction?.Invoke(ScanStarted);
                await DetectSheetAsync(sheetName, StandardMessageAction).ConfigureAwait(false);
                StandardMessageAction?.Invoke(ScanEnded);
            }
        }

        await foreach (IImportJob job in _scanner.CreateJobs(tabsToImport, SheetPlanFor).ConfigureAwait(false))
        {
            DatabaseTypeChooser? chooser = GetTypeChooser(job.SourceSheetName ?? string.Empty)
                ?? new DatabaseTypeChooser();
            yield return new DbImportJob(job.AsReader, chooser)
            {
                SourceSheetName = job.SourceSheetName
            };
        }
    }

    public async IAsyncEnumerable<ImportStepHelper> ImportFromFileStepByStep(
        DatabaseTypeEnum databaseTypeEnum,
        IDatabaseWithSpecificImportService databaseService,
        string schemaName,
        string databasaTableName,
        Action<string, string>? adColumnInfo = null,
        Action<IReadOnlyList<string[]>>? previewAction = null)
    {
        IReadOnlyList<ImportValidationError> validationErrors = await ValidateSelectedSheetsAsync();
        if (validationErrors.Count > 0)
        {
            throw new ImportValidationException(validationErrors);
        }

        DatabaseKind kind = databaseTypeEnum.ToDatabaseKind();
        int i = 0;
        await foreach (DbImportJob importJob in ReadFileAndReturnSingleImportJobs().ConfigureAwait(false))
        {
            string name = TabularImportScanner.BuildTableName(kind, schemaName, databasaTableName, i);

            if (importJob.ColumnTypesBestMatch is not null)
            {
                for (int j = 0; j < importJob.ColumnTypesBestMatch.Length; j++)
                {
                    adColumnInfo?.Invoke(importJob.ColumnHeadersNames[j], importJob.ColumnTypesBestMatch[j].ToString());
                }
            }

            previewAction?.Invoke(importJob.PreviewRows ?? []);
            yield return new ImportStepHelper()
            {
                Func = () => databaseService.DbSpecificImportPart(importJob, name, StandardMessageAction),
                ImportJob = importJob
            };
            i++;
        }
    }

    public async Task ImportFromFileAllSteps(DatabaseTypeEnum databaseType, IDatabaseWithSpecificImportService databaseService, string? schemaName, string databasaTableName)
    {
        try
        {
            IReadOnlyList<ImportValidationError> validationErrors = await ValidateSelectedSheetsAsync();
            if (validationErrors.Count > 0)
            {
                throw new ImportValidationException(validationErrors);
            }

            DatabaseKind kind = databaseType.ToDatabaseKind();
            int i = 0;
            await foreach (DbImportJob importJob in ReadFileAndReturnSingleImportJobs().ConfigureAwait(false))
            {
                string name = TabularImportScanner.BuildTableName(kind, schemaName, databasaTableName, i);
                await databaseService.DbSpecificImportPart(importJob, name, StandardMessageAction);
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
    }
}
