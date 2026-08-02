# Changelog

All notable changes to JustyBase are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- Root MIT `LICENSE` (+ EN/PL copies), restored `CONTRIBUTING.md`, `SECURITY.md`, `CHANGELOG.md`
- `docs/ARCHITECTURE.md` — public layering and SQL run overview
- ProDataGrid sibling detection via `Directory.Build.props` + CI checkout of `wieslawsoltes/ProDataGrid`
- Removed the configurable update feed path from the app configuration surface
- ISP split: `IDatabaseConnectionInfo`, `IDatabaseSchemaQueryService`, `IDatabaseDdlTextService` under umbrella `IDatabaseService`
- Portfolio tests: product Avalonia headless smokes, `SqlResultsViewModel` / `SqlFoldingStrategy` / Sqlite smoke, formatter/linter goldens
- `SqliteProductPipelineTests` — connect → run → results → CSV export without Netezza (via `DatabaseServiceHelpers` + Microsoft.Data.Sqlite)
- `docs/PLUGIN_CAPABILITIES.md` — stable / experimental / stub matrix for DB engines
- `DatabaseServiceRegistry` — instance-owned connection cache/factories; DI singleton via `IDatabaseServiceResolver`

### Changed

- Publish scripts use repo-relative paths (no machine-specific roots)
- README quick start, badges, dependency notes, and honest multi-DB maturity labels
- README Tests section notes Cobertura coverage CI artifact
- `ViewLocator` takes `IServiceProvider` (registered from `App` after DI build)
- Partial ViewModels moved out of `ViewModels/Shared` into `Documents/` / `Tools/` (`AddNewConnectionViewModel.Connection`, `DbSchemaViewModel.Schema`, `SchemaSearchViewModel.Search`)
- Extracted `SqlDocumentViewModel.RunStatus`, `SqlCodeEditor.Folding`, and `DatabaseService` Schema/Ddl/Import partials
- App services/VMs prefer `IDatabaseServiceResolver` over static cache helpers
- Renames: `ProcedureCachedInfo`, `ParquetFileWriterFromDataReader`, `GetKeyUniqueCodeText`, `Public.Lib.Services`
- Renames: `HandleExceptions`, `SqlParameterViewModel` / `SqlParameterWindow`, `PipeCommunicationService`, `ThinkSuppressingChatClient`

### Security

- Removed hardcoded Velopack/Object Storage pre-auth URL from source; documented rotation steps in `SECURITY.md` (invalidate any historical preauth token in Oracle Object Storage)

## [Prior]

See [GitHub Releases](https://github.com/justybase/justybase/releases) for packaged builds and release notes.
