# Result store and spill

Large query results are kept inside one application-wide storage budget. When a
result no longer fits, JustyBase streams it into a typed SQLite spill file in the
temporary directory and pages the grid from disk. This keeps result tabs open
without letting them exhaust managed memory or fill the disk silently.

## Limits and defaults

| Setting (`AppOptions`) | Default | UI range | Purpose |
|---|---:|---:|---|
| `ResultRowsLimit` | 1,000,000 | 100 – 10,000,000 (normalized to ≥ 1,000,000) | Hard per-result row cap; the reader is cancelled at the limit. |
| `ResultMemoryBudgetBytes` | 4 GiB | 512 – 65,536 MiB | Shared budget for decoded rows across every result tab. |
| `ResultSpillThreshold` | 250,000 | 10,000 – 10,000,000 | Row count that forces an in-memory result into spill mode. |
| `ResultSpillPageSize` | 20,000 | 1,000 – 100,000 | Rows read per grid page from the spill file. |
| `ResultSpillQuotaBytes` | 2 GiB | 256 – 65,536 MiB | Temporary-storage quota (estimated from row sizes) shared by all spilled results. |

The settings are exposed under **Settings → Limits**. `AppOptions.AddDefaultValues`
backfills missing values and clamps them to the ranges above. The spill threshold
and page size apply to the next result load; the memory budget and disk quota are
read when the application starts.

Spill mode is also entered when the shared memory budget cannot serve the next
row, even if the result is below `ResultSpillThreshold`. This is what lets
several medium result tabs stay open without exhausting memory.

## Storage layout

Each spill file stores one typed column per result column (`c0` … `cN`) instead
of one JSON payload per row:

| Result type | SQLite storage |
|---|---|
| Boolean, integral types | `INTEGER` |
| Single / Double | `REAL` |
| Decimal, UInt64 | exact `TEXT` plus a hidden `REAL` sort column |
| DateTime | sortable ISO-like `TEXT` |
| String / Char | `TEXT` |
| Binary | `BLOB` |

Rows are written in one transaction per batch (the whole result load). On
dispose, the file and its `-wal` / `-shm` sidecars are removed.

## Paging, sorting and filtering

- `ResultSpillStore.ReadPage` runs `SELECT … LIMIT/OFFSET` with `ORDER BY` built
  from `SpillSortKey`s. The row id is always the last tie-breaker so paging never
  repeats or skips rows.
- Grid sorting is translated into spill `ORDER BY` while in spill mode; the sort
  resets paging to page 1.
- Grid search is translated into a parameterized `WHERE` clause over every
  non-binary column (`LIKE … ESCAPE` for contains, equality otherwise). Column
  filter descriptors are translated too: equality/inequality, comparisons,
  contains/starts-with/ends-with, `IN` (including selected `NULL` values) and
  `Between`. Only custom predicates stay limited to the visible page.
- Filter values are always SQL parameters. SQL text is built only from generated
  column identifiers.
- Filtering, sorting and paging run the SQLite query on a worker thread and then
  apply the page on the UI thread. An `AsyncMutex` serializes spill operations so
  filter/sort/page actions apply in order, and a version counter drops work whose
  store was disposed or replaced.

## Export and clipboard

The grid keeps only one page in memory while a result is spilled. Export and
"copy all rows" therefore do **not** use the bound table; they create a
`SpillResultReader` over the store, which streams every filtered row in the active
sort order as a `DbDataReader`. That reader feeds the existing CSV, TSV, JSON,
XML, Parquet, SQL INSERT and Excel writers, plus the tab-delimited clipboard and
"copy as HTML" paths. Selections and single-cell copies still operate on the
visible page, as expected.

While an export streams, the status bar reports `Exporting… N rows` and a
**Cancel export** button is shown. Cancelling stops the reader at the next
row/page, removes the partially written file (when it was created by this
export), and restores the normal result status.

## Grouping spilled results

Grouping walks every item, so it cannot run on one visible page. When the user
groups a spilled result, `TryMaterializeSpillForGroupingAsync` loads the filtered
rows into memory on a worker thread and disposes the spill immediately, but only
when:

- the filtered row count is below 1,000,000, and
- the shared memory budget can hold the rows.

Otherwise the user gets an explanatory message and the grid stays paged.

## Disk quota and cleanup

- `SpillStoreManager` owns the application-wide quota. A write that would exceed
  it throws `SpillQuotaExceededException`; rows already committed are kept and
  the grid reports that the quota was reached. Results are never discarded to
  make room. The quota is charged from the same row-size estimate used by the
  memory budget, so actual SQLite file size can differ slightly.
- A shared LRU cache (`SpillPageCache`, 64 MiB) keeps decoded pages for scrolling
  without pinning an unbounded number of pages per tab. It is invalidated on
  filter changes and when a store is disposed.
- Spill files are named `spill_{pid}_{guid}.db`. At startup the manager removes
  files whose owning process is gone and files older than 24 hours. Files owned
  by the current process and unrelated files in the directory are never touched.

## Key files

- `source/JustyBase/Services/Results/ResultMemoryBudget.cs` — shared byte budget.
- `source/JustyBase/Services/Results/ResultRowSizer.cs` — row-size estimate.
- `source/JustyBase/Services/Results/ResultStoreService.cs` — DI singleton wiring.
- `source/JustyBase/Services/Results/SpillStoreManager.cs` — quota, cleanup, page cache.
- `source/JustyBase/Services/Results/ResultSpillSchema.cs` — typed schema and filters.
- `source/JustyBase/Services/Results/ResultSpillStore.cs` — SQLite read/write store.
- `source/JustyBase/Helpers/SpillResultReader.cs` — streaming `DbDataReader` for export/clipboard.
- `source/JustyBase/Helpers/AsyncMutex.cs` — serializes async spill operations.
- `source/JustyBase/ViewModels/Tools/SqlResultsViewModel.DataLoading.cs` — load, reserve, spill activation.
- `source/JustyBase/ViewModels/Tools/SqlResultsViewModel.GridInteraction.cs` — spill sort, filter and grouping.
