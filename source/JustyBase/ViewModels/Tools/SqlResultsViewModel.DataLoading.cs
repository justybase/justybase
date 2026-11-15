using System.Data;
using System.Data.Common;
using System.Diagnostics;
using JustyBase.Helpers;
using JustyBase.Models;
using JustyBase.PluginCommon.Contracts;
using JustyBase.Services.Results;

namespace JustyBase.ViewModels.Tools;

partial class SqlResultsViewModel
{
    private const int INITIAL_ROWS_LIMIT = 500;
    private const int MIN_SPILL_THRESHOLD = 10_000;
    private const int MIN_SPILL_PAGE_SIZE = 1_000;
    private const int MAX_SPILL_PAGE_SIZE = 100_000;
    private const int MIN_RESULT_ROWS_LIMIT = 50_000;
    private const int MAX_GROUPABLE_ROWS = 1_000_000;

    private ResultSpillStore? _spillStore;
    private IReadOnlyList<SpillSortKey> _spillSort = [];
    private SpillFilter? _spillFilter;
    private bool _applyingSpillFilter;
    private readonly AsyncMutex _spillOperationGate = new();
    private int _spillOperationVersion;
    private string? _appliedSpillFilterSignature;

    public void LoadData((IDatabaseService dbService, DbDataReader rdr, string errorMessage) res)
    {
        DisposeSpill();

        // A new query starts with a clean filter state; stale colN descriptors
        // must not apply to a different result set.
        _messageForUserTools.DispatcherActionInstance(FilteringModel.Clear);

        if (!string.IsNullOrWhiteSpace(res.errorMessage))
        {
            _messageForUserTools.DispatcherActionInstance(() =>
            {
                ErrorMessage = res.errorMessage;
            });
            return;
        }

        var reader = res.rdr;
        if (reader == null || !reader.HasRows && reader.FieldCount <= 0)
        {
            return;
        }

        var headers = new List<string>(reader.FieldCount);
        var dtnames = new List<string>(reader.FieldCount);
        var typeCodes = new List<TypeCode>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; ++i)
        {
            headers.Add(reader.GetName(i));
            string typeName = reader.GetDataTypeName(i);
            typeName ??= reader.GetFieldType(i).Name;
            dtnames.Add(typeName);
            if (typeName == "int1")
            {
                typeCodes.Add(TypeCode.Byte);
            }
            else
            {
                typeCodes.Add(Type.GetTypeCode(reader.GetFieldType(i)));
            }
        }
        var st = reader.GetSchemaTable();

        if (st is not null && st.Columns.Contains("NumericScale"))
        {
            CurrentResultsTable.NumericScales = new byte[reader.FieldCount];
            int nm = 0;
            foreach (var item in st.Rows.OfType<DataRow>())
            {
                var scale = item["NumericScale"];
                if (scale is not null && scale != DBNull.Value)
                {
                    try
                    {
                        byte byteScale = (byte)Math.Clamp(Convert.ToInt32(scale, System.Globalization.CultureInfo.InvariantCulture), 0, 127);
                        CurrentResultsTable.NumericScales[nm] = (byteScale == 127) ? (byte)8 : byteScale;
                    }
                    catch (Exception ex)
                    {
                        _simpleLogger.TrackError(ex, isCrash: false);
                        CurrentResultsTable.NumericScales[nm] = 0;
                    }
                }
                nm++;
            }
        }
        for (int i = headers.Count - 1; i >= 0; i--)
        {
            var ch = headers[i];
            int cnt = headers.Count(o => o == ch);
            if (cnt > 1)
            {
                headers[i] = ch + $"_{cnt}";
            }
        }

        var rows = new List<TableRow>();
        CurrentResultsTable.Headers = headers;
        CurrentResultsTable.DataTypeNames = dtnames;
        CurrentResultsTable.TypeCodes = typeCodes;
        CurrentResultsTable.Rows = rows;

        ViewBridge?.RefreshColumns();

        _messageForUserTools.DispatcherActionInstance(() =>
        {
            GridVisible = false;
            DataLoadingInProgress = true;
            LoadingPlaceholderMessage = "Loading preview…";
            RowsLoadingMessage = "Loading…";
        });

        try
        {
            int a = 0;
            lock (_lock)
            {
                var drr = res.dbService.GetDatabaseRowReader(reader);
                while (a++ < INITIAL_ROWS_LIMIT && reader.Read())
                {
                    object?[] fields = drr.ReadOneRow();
                    rows.Add(new TableRow { Fields = fields });

                    // The preview is bounded, so a full budget must not hide it;
                    // the reservation is best-effort and LoadRest spills next.
                    TryReserveInMemory(ResultRowSizer.Estimate(fields));
                }
            }
        }
        finally
        {
            // Preview first page immediately (single bulk notification), rest arrives in LoadRest.
            // Empty schema-only results skip LoadRest (!HasRows), so clear loading here.
            _messageForUserTools.DispatcherActionInstance(() =>
            {
                PublishFilteredRows(rows, replace: true);
                LoadingPlaceholderMessage = "";
                GridVisible = true;
                RowsLoadingMessage = $"{CurrentResultsTable.FilteredRows.Count:N0} rows";
                if (rows.Count == 0)
                {
                    DataLoadingInProgress = false;
                }
            });
        }
    }

    public void LoadRest(IDatabaseService? dbService, DbDataReader reader, int queryNum, ref int abortUbound, DbCommand command)
    {
        List<TableRow> rowsTemp = [];
        int i1 = CurrentResultsTable.Rows.Count;
        int spillThreshold = Math.Clamp(
            _generalApplicationData.Config.ResultSpillThreshold,
            MIN_SPILL_THRESHOLD,
            int.MaxValue);
        int pageSize = Math.Clamp(_generalApplicationData.Config.ResultSpillPageSize, MIN_SPILL_PAGE_SIZE, MAX_SPILL_PAGE_SIZE);
        int effectiveResultRowsLimit = Math.Max(MIN_RESULT_ROWS_LIMIT, _generalApplicationData.Config.ResultRowsLimit);
        ResultSpillStore? spill = null;
        bool spillActivated = false;
        string? spillNotice = null;
        int previewRows = i1;

        try
        {
            var drr = dbService.GetDatabaseRowReader(reader);
            long startTime = Stopwatch.GetTimestamp();

            // Peek the first remaining row so "loading more…" is only shown when more rows
            // actually exist (e.g. SELECT … LIMIT 1 must not advertise rows that do not come).
            // Sync Invoke: background reader must barrier on UI before continuing the DB loop.
            if (queryNum >= abortUbound && reader.Read())
            {
                int previewShown = previewRows;
                Dispatcher.UIThread.Invoke(() =>
                {
                    DataLoadingInProgress = true;
                    LoadingPlaceholderMessage = "";
                    RowsLoadingMessage = $"Preview {previewShown:N0} rows · loading more…";
                }, DispatcherPriority.Background);

                bool hasRow = true;
                while (hasRow && queryNum >= abortUbound)
                {
                    object?[] fields = drr.ReadOneRow();
                    i1++;

                    if (!spillActivated && spillNotice is null)
                    {
                        bool reserved = TryReserveInMemory(ResultRowSizer.Estimate(fields));
                        if (!reserved || i1 > spillThreshold)
                        {
                            try
                            {
                                ActivateSpill();
                            }
                            catch (SpillQuotaExceededException ex)
                            {
                                spillNotice = BuildSpillQuotaMessage(ex, i1);
                                break;
                            }
                        }
                    }

                    if (spillActivated)
                    {
                        try
                        {
                            spill!.WriteRow(fields);
                        }
                        catch (SpillQuotaExceededException ex)
                        {
                            spillNotice = BuildSpillQuotaMessage(ex, i1);
                            break;
                        }
                    }
                    else
                    {
                        rowsTemp.Add(new TableRow { Fields = fields });
                    }

                    if (i1 == effectiveResultRowsLimit)
                    {
                        command.Cancel();
                        abortUbound = queryNum + 1;
                        break;
                    }
                    if (i1 % 10_000 == 0 && Stopwatch.GetElapsedTime(startTime).TotalSeconds >= 1)
                    {
                        startTime = Stopwatch.GetTimestamp();
                        int localI = i1;
                        int localPreview = previewRows;
                        bool localSpill = spillActivated;
                        _messageForUserTools.DispatcherActionInstance(() =>
                        {
                            RowsLoadingMessage = localSpill
                                ? $"{localI:N0} rows (spilling…)"
                                : $"Preview {localPreview:N0} rows · loaded {localI:N0}…";
                        }, DispatcherPriority.Background);
                    }

                    hasRow = reader.Read();
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            spillNotice ??= $"Result loading stopped: {ex.Message}";
            _simpleLogger.TrackError(ex, isCrash: false);
        }
        finally
        {
            if (spillActivated && spill is not null)
            {
                try
                {
                    spill.EndWriteBatch();
                    DisposeSpill();
                    _spillStore = spill;
                    spill = null;
                    IsSpillMode = true;
                    SpillPageSize = pageSize;
                    SpillPageIndex = 0;
                    SpillTotalRows = _spillStore.RowCount;
                    SpillFilteredRows = _spillStore.FilteredRowCount;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    spillNotice ??= $"Spill finalization failed: {ex.Message}";
                    _simpleLogger.TrackError(ex, isCrash: false);
                    try
                    {
                        spill.Dispose();
                    }
                    catch (Exception cleanupEx) when (cleanupEx is not OutOfMemoryException)
                    {
                        // best effort
                    }
                    spill = null;
                    spillActivated = false;
                }
            }
            else
            {
                IsSpillMode = false;
                if (rowsTemp.Count > 0)
                {
                    lock (_lock)
                    {
                        CurrentResultsTable.Rows.AddRange(rowsTemp);
                    }
                }
            }

            // Sync Invoke: must finish detach → bulk publish → reattach before LoadRest returns.
            Dispatcher.UIThread.Invoke(() =>
            {
                // Detach only for the bulk append so the visible preview does not re-layout per row.
                ViewBridge?.SuspendGridBinding();

                if (IsSpillMode)
                {
                    _spillSort = [];
                    _spillFilter = null;
                    ApplySpillPageRows(_spillStore!.ReadPage(0, SpillPageSize, _spillSort), 0);
                    RowsLoadingMessage = spillNotice is null
                        ? BuildSpillStatusMessage()
                        : $"{BuildSpillStatusMessage()} · {spillNotice}";
                }
                else if (rowsTemp.Count > 0)
                {
                    PublishFilteredRows(rowsTemp, replace: false, rebuildIndexMap: true);
                    rowsTemp.Clear();
                    RowsLoadingMessage = spillNotice ?? $"{CurrentResultsTable.FilteredRows.Count:N0} rows";
                }
                else
                {
                    lock (_lock)
                    {
                        CurrentResultsTable.RebuildRowIndexMap();
                    }

                    if (spillNotice is not null)
                    {
                        RowsLoadingMessage = spillNotice;
                    }
                }

                GridCollectionView = new Avalonia.Collections.DataGridCollectionView(CurrentResultsTable.FilteredRows);
                ViewBridge?.ResumeGridBinding(clearSelection: true);
                GridVisible = true;
                DataLoadingInProgress = false;
                LoadingPlaceholderMessage = "";
                GridEnabled = true;
                NotifySpillCommands();
                ViewBridge?.InvalidateSummaryLayout();
                RefreshFind();
            }, DispatcherPriority.Background);
        }

        void ActivateSpill()
        {
            var schema = ResultSpillSchema.FromTable(CurrentResultsTable);
            ResultSpillStore created = _resultStore.CreateSpillStore(schema);
            try
            {
                created.PageSize = pageSize;
                created.BeginWriteBatch();
                foreach (var existing in CurrentResultsTable.Rows)
                {
                    created.WriteRow(existing.Fields);
                }
                foreach (var pending in rowsTemp)
                {
                    created.WriteRow(pending.Fields);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                created.Dispose();
                throw;
            }

            rowsTemp.Clear();
            spill = created;
            spillActivated = true;
            ReleaseBudget();
        }
    }

    private void PublishFilteredRows(IReadOnlyList<TableRow> rows, bool replace, bool rebuildIndexMap = true)
    {
        lock (_lock)
        {
            if (replace)
            {
                CurrentResultsTable.FilteredRows.ReplaceAll(rows);
            }
            else
            {
                CurrentResultsTable.FilteredRows.AddRange(rows);
            }

            if (rebuildIndexMap)
            {
                CurrentResultsTable.RebuildRowIndexMap();
            }
        }
    }

    private async Task ApplySpillPageAsync(int pageIndex)
    {
        ResultSpillStore? store = _spillStore;
        if (store is null)
        {
            return;
        }

        int pageSize = SpillPageSize;
        IReadOnlyList<SpillSortKey> sort = _spillSort;

        try
        {
            using (await _spillOperationGate.LockAsync())
            {
                int version = Interlocked.Increment(ref _spillOperationVersion);
                if (!ReferenceEquals(store, _spillStore))
                {
                    return;
                }

                int total = store.FilteredRowCount;
                int pageCount = pageSize <= 0 || total == 0 ? 0 : (total + pageSize - 1) / pageSize;
                int clamped = Math.Clamp(pageIndex, 0, Math.Max(0, pageCount - 1));

                IReadOnlyList<object?[]> page = await Task.Run(() => store.ReadPage(clamped, pageSize, sort));
                if (version != Volatile.Read(ref _spillOperationVersion) || !ReferenceEquals(store, _spillStore))
                {
                    return;
                }

                ApplySpillPageRows(page, clamped);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _simpleLogger.TrackError(ex, isCrash: false);
            RowsLoadingMessage = $"Paging failed: {ex.Message}";
        }
    }

    private void ApplySpillPageRows(IReadOnlyList<object?[]> page, int pageIndex)
    {
        SpillPageIndex = pageIndex;
        var rows = page.Select(fields => new TableRow { Fields = fields! }).ToList();

        lock (_lock)
        {
            CurrentResultsTable.Rows.Clear();
            CurrentResultsTable.Rows.AddRange(rows);
            CurrentResultsTable.FilteredRows.ReplaceAll(rows);
            CurrentResultsTable.RebuildRowIndexMap();
        }
    }

    /// <summary>
    /// Loads every spilled row that matches the active filter into memory so the
    /// existing grouping code can run. Refused when the filtered result would
    /// exceed the shared budget or the grouping row limit.
    /// </summary>
    private async Task<bool> TryMaterializeSpillForGroupingAsync()
    {
        ResultSpillStore? store = _spillStore;
        if (store is null)
        {
            return true;
        }

        int expected = store.FilteredRowCount;
        if (expected >= MAX_GROUPABLE_ROWS)
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance("Too many items to group");
            return false;
        }

        long estimate = store.EstimatedBytes;
        if (estimate > _resultStore.Budget.AvailableBytes)
        {
            ShowGroupingBudgetMessage(estimate);
            return false;
        }

        if (!_resultStore.Budget.TryReserve(estimate))
        {
            ShowGroupingBudgetMessage(estimate);
            return false;
        }

        _budgetReservedBytes += estimate;

        using (await _spillOperationGate.LockAsync())
        {
            Interlocked.Increment(ref _spillOperationVersion);
            IReadOnlyList<SpillSortKey> sort = _spillSort;
            if (!ReferenceEquals(store, _spillStore))
            {
                ReleaseGroupingReservation(estimate);
                return false;
            }

            List<object?[]> raw;
            try
            {
                raw = await Task.Run(() => store.ReadAll(sort).ToList());
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                ReleaseGroupingReservation(estimate);
                _simpleLogger.TrackError(ex, isCrash: false);
                _messageForUserTools.ShowSimpleMessageBoxInstance($"Could not load spilled rows for grouping: {ex.Message}");
                return false;
            }

            if (!ReferenceEquals(store, _spillStore))
            {
                ReleaseGroupingReservation(estimate);
                return false;
            }

            long actual = raw.Sum(fields => ResultRowSizer.Estimate(fields));
            if (actual > estimate)
            {
                long extra = actual - estimate;
                if (_resultStore.Budget.TryReserve(extra))
                {
                    _budgetReservedBytes += extra;
                }
            }
            else if (actual < estimate)
            {
                long unused = estimate - actual;
                _budgetReservedBytes -= unused;
                _resultStore.Budget.Release(unused);
            }

            _spillStore = null;
            store.Dispose();
            _spillSort = [];
            _spillFilter = null;
            _appliedSpillFilterSignature = null;
            IsSpillMode = false;
            SpillTotalRows = 0;
            SpillFilteredRows = 0;
            SpillPageIndex = 0;

            var rows = new List<TableRow>(raw.Count);
            foreach (object?[] fields in raw)
            {
                rows.Add(new TableRow { Fields = fields });
            }

            lock (_lock)
            {
                CurrentResultsTable.Rows = rows;
                CurrentResultsTable.FilteredRows.ReplaceAll(rows);
                CurrentResultsTable.RebuildRowIndexMap();
            }

            GridCollectionView = new Avalonia.Collections.DataGridCollectionView(CurrentResultsTable.FilteredRows);
            RowsLoadingMessage = $"{rows.Count:N0} rows materialized for grouping";
            NotifySpillCommands();
            return true;
        }
    }

    private void ShowGroupingBudgetMessage(long estimate)
    {
        _messageForUserTools.ShowSimpleMessageBoxInstance(
            $"Grouping needs about {estimate / (1024.0 * 1024.0):N0} MiB but only " +
            $"{_resultStore.Budget.AvailableBytes / (1024.0 * 1024.0):N0} MiB is available. " +
            "Close another result tab or narrow the query.");
    }

    private void ReleaseGroupingReservation(long estimate)
    {
        _budgetReservedBytes -= estimate;
        _resultStore.Budget.Release(estimate);
    }

    private void DisposeSpill()
    {
        Interlocked.Increment(ref _spillOperationVersion);
        ReleaseBudget();
        _spillStore?.Dispose();
        _spillStore = null;
        _spillSort = [];
        _spillFilter = null;
        _appliedSpillFilterSignature = null;
        IsSpillMode = false;
        SpillTotalRows = 0;
        SpillFilteredRows = 0;
        SpillPageIndex = 0;
    }

    private bool TryReserveInMemory(long bytes)
    {
        if (!_resultStore.Budget.TryReserve(bytes))
        {
            return false;
        }

        _budgetReservedBytes += bytes;
        return true;
    }

    private void ReleaseBudget()
    {
        long bytes = _budgetReservedBytes;
        if (bytes <= 0)
        {
            return;
        }

        _budgetReservedBytes = 0;
        _resultStore.Budget.Release(bytes);
    }

    private static string BuildSpillQuotaMessage(SpillQuotaExceededException ex, int loadedRows)
        => $"Spill quota reached after {loadedRows:N0} rows; close another result or raise the quota "
           + $"({ex.QuotaBytes / (1024.0 * 1024.0):N0} MiB configured).";
}
