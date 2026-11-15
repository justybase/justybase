# Changelog

All notable changes to JustyBase are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [1.4.5] - 2026-11-15

### Fixed

- Grouping a result grid no longer collapses every row into a single `<NULL>` group in the Native AOT Windows build. `DataGridPathGroupDescription` resolves `Fields[index]` through reflection that the trimmer removes, so the grid fell back to using the row object as the group key and rendered `TableRow.EMPTY_NAME_PLACEHOLDED` as `<NULL>`. Grouping now uses `ResultGridGroupingFactory`, which reads the column through a compiled delegate and keeps NULLs in one shared sentinel group.
- Grouping a result grid no longer throws `InvalidCastException` when the reported column `TypeCode` disagrees with the runtime values (for example `Int32` values in a column typed as string). Grouped rows are ordered by the runtime value instead of a typed cast.

## [1.4.4] - 2026-11-15

### Changed

- Nullable migration finished: `source/JustyBase` now compiles with full `Nullable=enable` (flow analysis), and every nullable warning was resolved with real null guards instead of suppressions. Shared dialog helpers (`ShowDialogOrShow`) keep modal behaviour when a main window exists and degrade to non-modal windows in designer/headless paths.
- `IHotDocumentVm.FilePath`/`ResetFontStyle`, `TableRow.Fields` and `IDatabaseSchemaQueryService.IsItemSourceContains` now express their real nullability (`string?`/`object?[]`/`Regex?`).
- Bumped `GitHub.Copilot.SDK` to 1.0.34, `LiveMarkdown.Avalonia` to 2.7.0 and `JustyBase.Db2.IBMCompat` to 0.1.0-alpha.5 (`THIRD_PARTY_NOTICES.md` regenerated).

### Fixed

- Comment lines / rename commands, font reset, results export, schema search, file explorer pickers and the AI chat schema tools no longer hit null-reference paths when the editor, window or database service is not ready.
- Restored `CascadingDistinctValueFilterContext.PropertyPath`, which a refactor had dropped from the constructor while the field stayed `readonly`-unassigned, so column filters silently lost their property path.
- Restored the SQLite database icon in the schema tree (`DatabaseIconConverter` lost `_sqliteIcon16` and assigned the table bitmap twice).
- Reattached `ResetFontStyle` when a document's editor is recreated, so a reused tab applies the configured font again.

## [1.4.3] - 2026-11-15

### Fixed

- Large Netezza result sets no longer open a new result tab for every 500-row block: row blocks of the same query are appended to the grid that is already loading.
- The schema tree shows its "Loading…" row only when a node takes longer than about 120 ms to load, and applies the requested expansion once the children arrive instead of requiring a second click.

## [1.4.2] - 2026-11-15

### Security

- CI now fails on any known vulnerable NuGet package (`dotnet list package --vulnerable --include-transitive`), and `NU1902`/`NU1903` are build errors.
- Pinned `System.Security.Cryptography.Pkcs` 11.0.0 to remove the vulnerable 8.0.1 build pulled transitively by `Oracle.ManagedDataAccess.Core` (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7).
- Releases publish a `SHA256SUMS` manifest covering every uploaded artifact. Authenticode signing and macOS notarization remain future work.

### Added

- `THIRD_PARTY_NOTICES.md`, generated from the production dependency graphs by `scripts/generate-third-party-notices.ps1`, with an **About → Third-party notices** dialog in the application.
- CI gates for `dotnet format --verify-no-changes` (whitespace and style) and a Linux build of the application.
- Definition index with lexer-aware "Find usages", three-level impact analysis and definition search (line numbers and snippets; shared by Schema Search)
- Db2 LUW session monitor via `SYSPROC.MON_GET_CONNECTION` with `FORCE APPLICATION` termination
- Cross-connection table copy dialog with column mapping, target table creation and streamed parameterized inserts
- JSON, XML, TSV and SQL INSERT result exports, plus schema "Export DDL + data…" scripts
- Live Db2 host-layer tests (tree, capability-gated menu, DDL) and the sibling Db2 parser/linter live test project in `JustyBase.slnx`
- Db2 lint rules renamed to `DB2L001`–`DB2L008` to avoid collisions with Rust language diagnostics
- Postgres driver in the Slim variant (now Netezza + SQLite + DB2 + Postgres, still no AI)
- MySQL driver in the Slim variant (now Netezza + SQLite + DB2 + Postgres + MySQL, still no AI)
- Result store rework: a shared 4 GiB memory budget, spill after 250,000 rows (or when the budget is full), typed SQLite columns with exact decimals, SQL-driven paging/sorting/filtering, grouping only after a budget-checked materialization, a 2 GiB disk quota that keeps already-written rows, a 64 MiB LRU page cache, and startup cleanup of orphaned spill files ([docs/RESULT_STORE.md](docs/RESULT_STORE.md))
- Spill-aware export and clipboard: a streaming `SpillResultReader` feeds CSV/TSV/JSON/XML/Parquet/SQL/Excel and "copy all rows"/HTML, so large results are no longer truncated to the visible page
- Spill filtering, sorting, paging and grouping now run their SQLite work off the UI thread with serialized, versioned operations that discard stale pages
- Progress reporting and a **Cancel export** action for long-running result exports; cancelling removes the partial file and restores the result status
- Settings → Others → **Distribution variant** switches an installed Windows build between Full (`win`) and Slim (`win-slim`); the other variant is downloaded with an explicit channel (downgrades allowed) and applied on restart, keeping connections, settings and open files

### Fixed

- Cleared the remaining `CS8604`, `CS8601` and `CS8765` nullability warnings across the application, editor, Netezza plugin, importer helpers and tests.
- `.editorconfig` section globs now use `folder/**`; the previous `folder/**/*.cs` pattern skipped files directly in the folder, so the documented test-project suppressions never applied (685 `CA1707` warnings).
- README no longer claims ProDataGrid 12.0.5 or .NET 10; both were stale.
- Excluded the non-shipped UCanAccess console utility from the production third-party notice scan so CI only requires restored assets for shipped projects.

## [1.4.1] - 2026-11-07

### Added

- Windows Velopack installer for the Slim variant on its own `win-slim` update channel (same `JustyBase` package id, channel-suffixed assets)

## [1.4.0] - 2026-11-07

### Added

- Slim release variant (Netezza + SQLite + DB2 only, `EnableAi=false`, static splash): portable ZIPs for Windows, Linux and macOS plus a Windows Velopack installer on its own `win-slim` update channel via `publishWindows.bat` mode 2 / `scripts/package-windows.ps1 -Slim` / `publishLinux.sh --slim` / `publishMacOS.sh --slim`
- Full release variant now includes every database driver (Oracle, MySQL, DuckDB) with AI chat on all platforms
- `EnableLottie` build flag and `LOTTIE` constant; `LiveMarkdown.Avalonia` now follows `EnableAi` with runtime-loaded styles (`App.LiveMarkdown.cs`)

### Removed

- Light Windows package variant (replaced by Slim)

## [1.3.20] - 2026-10-17

### Added

- Excel File SQL plugin for querying `.xlsx` and `.xlsb` worksheets as SQL tables, plus multi-result export to separate `.xlsx`/`.xlsb` worksheets
- Microsoft Access plugin using the local `JustyBase.UCanAccessCs` provider, including the Access SQL dialect and read-only connection option
- Root MIT `LICENSE` (+ EN/PL copies), restored `CONTRIBUTING.md`, `SECURITY.md`, `CHANGELOG.md`
- `docs/ARCHITECTURE.md` — public layering and SQL run overview
- ProDataGrid 12.0.5 NuGet dependency
- Velopack updates now use GitHub Releases directly; the feed URL remains fixed to the public JustyBase repository
- ISP split: `IDatabaseConnectionInfo`, `IDatabaseSchemaQueryService`, `IDatabaseDdlTextService` under umbrella `IDatabaseService`
- Portfolio tests: product Avalonia headless smokes, `SqlResultsViewModel` / `SqlFoldingStrategy` / Sqlite smoke, formatter/linter goldens
- `SqliteProductPipelineTests` — connect → run → results → CSV export without Netezza (via `DatabaseServiceHelpers` + Microsoft.Data.Sqlite)
- `docs/PLUGIN_CAPABILITIES.md` — stable / experimental / stub matrix for DB engines
- `DatabaseServiceRegistry` — instance-owned connection cache/factories; DI singleton via `IDatabaseServiceResolver`
- `ProductionExceptionHandlingGuardTests` — static guard forbidding empty catch blocks and capping broad `catch (Exception)` in production code
- `DatabaseService.SqlHelpers` — shared `EscapeSqlLiteral`, `EnsureSqlStatement` and `AppendSqlStatements` helpers used by Postgres/SQLite/DB2 plugins
- `SqlResultsViewModel.GridInteraction` — grouping, filter-search, single-column sort and selection statistics moved out of `SqlResultsView` code-behind
- `IDataGridClipboardService.BuildCopyWithHeadersText` — selection-aware clipboard text builder extracted from `SqlResultsView`
- `SqlResultsViewModel.Export` — `ExportAllResults` / `ActionFromButton` split into their own partial file; the clipboard text itself is now built by `IDataGridClipboardService.BuildSelectedCellsColumnText` / `BuildSelectedRangeText` / `BuildRowValuesText`
- `IResultGridColumnReorderService` — column drag/reorder rules (`CanReorderHeaders`, `CalculateNewDisplayIndex`) extracted from the header drop handler in `ColumnHeaderFactory`
- `SqlResultsView.Columns.cs` — DataGrid column construction and header template moved out of `SqlResultsView.axaml.cs`
- `ResultGridColumnSortRules` — sort-glyph toggle and "only columns with a `CustomResultComparer` participate" extracted from `ResultDataGrid_Sorting`
- `ResultGridRowHeaderRules` — 1-based culture-aware row numbering, `odd-row` striping and the group item-count format extracted from `DataGrid_LoadingRow` / `DataGrid_LoadingRowGroup`

### Changed

- Publish scripts use repo-relative paths (no machine-specific roots)
- Netezza/UCanAccess libraries are now **required** sibling checkouts referenced as projects; the NuGet fallback (`UseLocalJustyBaseLibraries` / `JustyBaseNetezzaLibsPackageVersion`) was removed. GitHub Actions workflows check out `justybase/JustyBase.NetezzaSql` and `justybase/JustyBase.UCanAccessCs` next to `JustyBase` before restore/build
- DB2 driver now resolves exclusively from the `JustyBase.Db2` + `JustyBase.Db2.IBMCompat` NuGet packages (default `0.1.0-alpha.1`); the local `../cs-db2` sibling checkout and the `UseLocalDb2Driver` switch were removed
- Windows release packaging switched to self-contained Native AOT (`aot-self-contained` variant, `PublishAot`/`PublishTrimmed`); Linux/macOS remain self-contained ReadyToRun
- Release packages are built with `-p:CopilotSkipCliDownload=true`, so the native GitHub Copilot CLI runtime (~200 MB) is no longer bundled; the Copilot AI-chat backend uses a separately installed `copilot` CLI on `PATH` (or `JUSTYBASE_COPILOT_COMMAND`). When the CLI is missing the AI Chat panel now shows an actionable install hint with a **Check again** button.
- `JustyBase.Ai` defaults to `CopilotSkipCliDownload=true`, so local development builds also omit the Copilot runtime unless overridden with `-p:CopilotSkipCliDownload=false`.
- README quick start, badges, dependency notes, and honest multi-DB maturity labels
- README Tests section notes Cobertura coverage CI artifact
- `ViewLocator` takes `IServiceProvider` (registered from `App` after DI build)
- Partial ViewModels moved out of `ViewModels/Shared` into `Documents/` / `Tools/` (`AddNewConnectionViewModel.Connection`, `DbSchemaViewModel.Schema`, `SchemaSearchViewModel.Search`)
- Extracted `SqlDocumentViewModel.RunStatus`, `SqlCodeEditor.Folding`, and `DatabaseService` Schema/Ddl/Import partials
- App services/VMs prefer `IDatabaseServiceResolver` over static cache helpers
- Renames: `ProcedureCachedInfo`, `ParquetFileWriterFromDataReader`, `GetKeyUniqueCodeText`, `Public.Lib.Services`
- Renames: `HandleExceptions`, `SqlParameterViewModel` / `SqlParameterWindow`, `PipeCommunicationService`, `ThinkSuppressingChatClient`
- Split `SettingsViewModel` into domain partials (`SettingsViewModel.Fim.cs`, `.EmbeddedChat.cs`, `.AiChat.cs`)
- Split `AiChatViewModel` into domain partials (`AiChatViewModel.Tooling.cs`, `.Backend.cs`, `.Composer.cs`)
- Postgres/SQLite/DB2 plugins now reuse the shared SQL helpers instead of local copies
- `SqlResultsView` reduced to a thin adapter; grid-interaction logic delegated to `SqlResultsViewModel` and the `IResultGrid*Service` services
- `SqlResultsView.MoveGroup` validates the drag through `IResultGridGroupingDragService.TryCreateMoveRequest` (previously injected but unused)
- `SqlResultsViewModel` now takes `IDataGridClipboardService` so export/toolbar actions can reuse the tested text builders
- Fixed `JustyBase.HeadlessTests` compile error (`ContentReplacement` was missing its `Changes` argument), so the product smoke tests build and run again

### Fixed

- `DatabaseSearchScopeHelper.ResolveDatabaseOrFirst` now treats a blank requested database (`""`/whitespace) as unqualified and falls back to the first/active database, instead of returning an empty name that never matches
- SQL completion hydrates columns for a `.`-qualified relation when its schema snapshot is still empty, even if a partial engine list already contains column items

### Security

- Removed hardcoded Velopack/Object Storage pre-auth URL from source; documented rotation steps in `SECURITY.md` (invalidate any historical preauth token in Oracle Object Storage)
- Connection passwords now migrate to the OS secret store (Windows Credential Manager, Secret Service via `secret-tool` on Linux, Keychain on macOS) with transparent file fallback; the credentials file becomes metadata-only (format v2) with a one-time `.pre-keychain-*` backup and rotating `.bak.1..3` generations. Downgrade note: a build that only understands v1 restores passwords from `.bak.1`, but a pre-versioning build cannot read the v2 envelope — keep a `.bak`/export if you plan to roll back across many versions
- `LoginDataDic` now returns snapshot copies (no more shared mutable credential state); service cache keys are case-insensitive; settings-only changes persist via `SaveAppConfig()` without rewriting the credentials file

## [Prior]

See [GitHub Releases](https://github.com/justybase/justybase/releases) for packaged builds and release notes.
