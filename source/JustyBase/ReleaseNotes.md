# JustyBase 1.4.4

- Finished the nullable migration of the desktop app: full flow analysis (`Nullable=enable`) with every nullable warning fixed by real null guards instead of suppressions.
- Fixed column filters losing their property path, the SQLite database icon in the schema tree, and the configured font not being re-applied when a tab's editor is recreated.
- Updated `GitHub.Copilot.SDK`, `LiveMarkdown.Avalonia` and the DB2 compatibility driver.

# JustyBase 1.4.3

- Fixed large Netezza result sets being split into one result tab per 500-row block; all blocks now fill a single grid.
- The schema tree shows its loading row only for slow loads and completes the requested node expansion when children arrive, so a fast node no longer needs a second click.

# JustyBase 1.4.2

- Added a Settings option to switch an installed Windows build between Full and Slim update channels.
- Reworked large-result storage with memory/disk budgets, typed spill storage, and asynchronous paging, sorting, filtering, and grouping.
- Added streaming exports with progress and cancellation, including for results larger than the visible page.
- Expanded DB2 support with definition search, impact analysis, session monitoring, and cross-connection table copy.
- Added SQL editor improvements including dialect-aware linting, ranked completions, signature help, and an asynchronous schema tree.
- Added SHA256SUMS manifests to release assets.

# JustyBase 1.4.1

- Added a Velopack installer for Slim (`win-slim` update channel), alongside the portable ZIP.
- Slim update checks stay on the Slim channel and never collide with Full.

# JustyBase 1.4.0

- Added Slim packages (Netezza + SQLite + DB2 only, no AI): smaller download as a portable ZIP and a Velopack installer with its own update channel for Windows, plus portable ZIPs for Linux and macOS.
- Full packages now include all database drivers (Oracle, MySQL, DuckDB) with AI chat.
- Slim uses a static splash screen; Full keeps the animated splash.

# JustyBase 1.3.20

- Improved DuckDB schema discovery and metadata handling.
- Updated Dock.Avalonia and DuckDB.NET dependencies.
- Added SQL querying and multi-result export for Excel workbooks, plus the Microsoft Access driver.
- Added cross-platform self-contained release packages and improved plugin selection.

# JustyBase 1.3.19

- Added SQL querying for `.xlsx` and `.xlsb` workbooks through the Excel file driver.
- Added `.xlsx` and `.xlsb` result export with each result set written to a separate worksheet.
- Added a dedicated Microsoft Access driver and SQL dialect for `.mdb` and `.accdb` files, backed by `JustyBase.UCanAccessCs`.
- Added Access read-only connection settings and schema/column metadata browsing.

# JustyBase 1.3.17

- Stabilized the Velopack single-instance regression test used by the release pipeline.

# JustyBase 1.3.16

- Fixed automatic restart after a Velopack update when the previous instance still owns the single-instance mutex.
- Added startup diagnostics for Velopack restart and single-instance coordination.

# JustyBase 1.3.15

- Test release for verifying the Velopack update and restart flow in an installed Windows build.

# JustyBase 1.3.14

- Fixed Velopack update application so downloaded updates are retried on the next startup if the updater cannot finish during restart.
- Added logging for failures while applying a downloaded update.

# JustyBase 1.3.13

- Fixed database icon rendering in schema and connection lists so high-resolution icons are scaled to the control bounds instead of being clipped.

# JustyBase 1.3.12

- Reworked the database connection wizard with searchable driver selection, driver-aware fields, inline validation, and connection testing.
- Added file database actions for SQLite and DuckDB: open an existing file, create a new file, or use an in-memory database.
- Added port persistence and driver-specific default ports for database connections.
- Moved connection testing off the UI thread so plugin loading and Netezza tests do not freeze the application.
- Replaced blurry database icons with 128x128 assets based on the JustyBase VS Code database icons.

# JustyBase 1.0.0-rc.11

- SQLite schema browsing now exposes tables, views, columns, indexes, triggers, foreign keys, attached catalogs, virtual tables, and strict-table metadata.
- Added SQLite DDL and diagnostics actions, including index/trigger DDL, drop scripts, integrity checks, and foreign-key checks.
- Added SQLite session options for read-only/immutable connections, ATTACH databases, shared in-memory databases, foreign-key enforcement, and busy timeouts.
- Applied SQLite connection configuration consistently to schema caching, SQL execution, imports, AI database access, session variables, and reconnects.
- Release packaging disables the shared compiler (`UseSharedCompilation=false`) to avoid cross-platform artifact file locks during publish.
