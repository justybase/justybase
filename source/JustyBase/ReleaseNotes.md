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
