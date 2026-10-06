# Changelog

All notable changes to JustyBase are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

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
- CI checks out the sibling `JustyBase.NetezzaSql` repo and builds tests/releases with local project references (`UseLocalJustyBaseLibraries=true`) instead of floating NuGet packages
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

### Security

- Removed hardcoded Velopack/Object Storage pre-auth URL from source; documented rotation steps in `SECURITY.md` (invalidate any historical preauth token in Oracle Object Storage)
- Connection passwords now migrate to the OS secret store (Windows Credential Manager, Secret Service via `secret-tool` on Linux, Keychain on macOS) with transparent file fallback; the credentials file becomes metadata-only (format v2) with a one-time `.pre-keychain-*` backup and rotating `.bak.1..3` generations. Downgrade note: a build that only understands v1 restores passwords from `.bak.1`, but a pre-versioning build cannot read the v2 envelope — keep a `.bak`/export if you plan to roll back across many versions
- `LoginDataDic` now returns snapshot copies (no more shared mutable credential state); service cache keys are case-insensitive; settings-only changes persist via `SaveAppConfig()` without rewriting the credentials file

## [Prior]

See [GitHub Releases](https://github.com/justybase/justybase/releases) for packaged builds and release notes.
