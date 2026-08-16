# JustyBase 1.0.0-rc.11

- SQLite schema browsing now exposes tables, views, columns, indexes, triggers, foreign keys, attached catalogs, virtual tables, and strict-table metadata.
- Added SQLite DDL and diagnostics actions, including index/trigger DDL, drop scripts, integrity checks, and foreign-key checks.
- Added SQLite session options for read-only/immutable connections, ATTACH databases, shared in-memory databases, foreign-key enforcement, and busy timeouts.
- Applied SQLite connection configuration consistently to schema caching, SQL execution, imports, AI database access, session variables, and reconnects.
- Release packaging disables the shared compiler (`UseSharedCompilation=false`) to avoid cross-platform artifact file locks during publish.
