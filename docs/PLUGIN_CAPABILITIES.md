# Database plugin capabilities

JustyBase is **Netezza-first**. Other engines are available with uneven maturity. Use this matrix when evaluating multi-DB support.

| Engine | Maturity | SQL intelligence | Schema groups | Table actions | Notes |
|--------|----------|------------------|---------------|---------------|-------|
| **Netezza (dotnet)** | **stable** | Netezza | Full | Full | Primary product path |
| **DB2** | **experimental** | Db2 | Tables, views, procedures, sequences, functions, synonyms, aggregates, indexes, partitions, triggers, tablespaces | Full except Netezza-only actions | Highly experimental: in-development managed DRDA driver (`JustyBase.Db2` 0.1.0, LUW-only). Do not use in production. External tables not implemented |
| **Postgres** | **stable** | PostgreSQL | Tables, views, procedures, sequences, functions, indexes, partitions | No deleted/grant/synonym/CTAS actions; VACUUM/ANALYZE replace GROOM/STATISTICS | Synonyms, aggregates, triggers and tablespaces are not exposed by the driver |
| **Oracle** | **experimental** | Oracle | Tables, views, procedures, functions, sequences, synonyms, indexes, triggers | Partial | Index/trigger DDL for existing objects is not implemented (browse only); statistics/GROOM hidden |
| **DuckDB** | **experimental** | PostgreSQL (approximation) | Tables, views | Partial | Loaded as optional plugin DLL; no procedures, synonyms, sequences, index/trigger catalogs |
| **MySQL** | **stub** | MySQL | Tables, views | Query/cleanup helpers only | Connection and query smoke only; DDL, procedures and synonyms are hidden until P4 |
| **SQLite** | **stable** | SQLite | Tables, views, indexes, triggers | Full for the SQLite object model | Native catalog browsing, foreign keys, ATTACH, SQLite DDL and diagnostics; no procedures/functions/sequences/synonyms |
| **Excel File SQL** | **experimental** | SQLite (authoring approximation) | Tables, views | Read-only | `.xlsx` and `.xlsb` worksheets are exposed as DuckDB-backed tables; export supports separate `.xlsx`/`.xlsb` result tabs |
| **Microsoft Access** | **experimental** | Access | Tables, views | Partial | `.mdb`/`.accdb` through `JustyBase.UCanAccessCs`; read-only by default; no comments, EXPLAIN or duplicates |

**Maturity legend**

- **stable** — suitable for day-to-day use of core IDE flows (connect, browse, run, common scripts)
- **experimental** — works for many flows; expect gaps and rough edges
- **stub** — connection / query smoke only; do not expect feature parity

SQLite, DuckDB, Excel File SQL and Microsoft Access may be loaded via the plugin directory rather than the default in-process registration list.

Schema context menus and tree groups are gated by `DatabaseCapabilities` (`JustyBase.PluginCommon/Models/DatabaseCapabilities.cs`). A flag may be enabled only when the action produces valid SQL or an explicit explanatory comment; actions that would throw, return an empty string or return the legacy `"not supported"` sentinel are hidden. `SupportsIndexes`/`SupportsTriggers` gate catalog browsing, while `SupportsIndexDdl`/`SupportsTriggerDdl` gate DDL for existing objects. `SupportsDropObject` and `SupportsConstraints` separate destructive/constraint actions from read-only DDL.

## SQL intelligence routing

`SqlDialectResolver` maps each connected engine to the lexer, parser, linter, authoring catalog and outline used by the editor:

| Engine | `SqlDialect` | Lint ids |
|--------|--------------|----------|
| Netezza | `Netezza` | `NZ001`+ / `NZP` procedure rules / `NZ1xx` |
| DB2 | `Db2` | `DB2L001`–`DB2L008` |
| PostgreSQL | `PostgreSql` | PostgreSQL version diagnostics only (no quality rules yet) |
| Oracle | `Oracle` | `ORA001`–`ORA004` |
| MySQL | `MySql` | none |
| DuckDB | `PostgreSql` (approximation until a dedicated dialect exists) | none |
| SQLite | `Sqlite` | `SQLITE001`–`SQLITE006` |
| Microsoft Access | `Access` | `ACC001`–`ACC005` |
| Excel File SQL | `Sqlite` (historical authoring approximation) | `SQLITE001`–`SQLITE006` |
| MsSqlTrusted | `Mssql` | `MSS001`–`MSS008` (no driver registered) |

The shared severity options `LintSeverityNz001`/`002`/`003` map by rule name onto the active dialect: Netezza `NZ001`–`NZ003`, Db2 `DB2L001`–`DB2L003`, Oracle `ORA001`–`ORA003`, SQL Server `MSS001`–`MSS003` and Access `ACC001`–`ACC003`. Netezza keeps its historical extra mappings (`NZ004`, `NZ005`, `NZ008`, `NZ011`, `NZ012`, `NZ013`, `NZ015`, `NZ025`, `NZ102` and the legacy `SQL043`–`SQL046` aliases). Disabling the linter turns off every rule in the active registry.

## Schema groups

| Group | Netezza | DB2 | PostgreSQL | Oracle | DuckDB | MySQL | SQLite | Access | Excel |
|---|---|---|---|---|---|---|---|---|---|
| Tables | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Views | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| External tables | ✓ | – | – | – | – | – | – | – | – |
| Procedures | ✓ | ✓ | ✓ | ✓ | – | – | – | – | – |
| Sequences | ✓ | ✓ | ✓ | ✓ | – | – | – | – | – |
| Functions | ✓ | ✓ | ✓ | ✓ | – | – | – | – | – |
| Synonyms | ✓ | ✓ | – | ✓ | – | – | – | – | – |
| Aggregate | ✓ | ✓ | – | – | – | – | – | – | – |
| Indexes | – | ✓ | ✓ | ✓ | – | – | ✓ | – | – |
| Partitions | – | ✓ | ✓ | – | – | – | – | – | – |
| Triggers | – | ✓ | – | ✓ | – | – | ✓ | – | – |
| Tablespaces | – | ✓ | – | – | – | – | – | – | – |
| Fluids | ✓ | – | – | – | – | – | – | – | – |

## Table actions

| Action | Netezza | DB2 | PostgreSQL | Oracle | DuckDB | MySQL | SQLite | Access | Excel |
|---|---|---|---|---|---|---|---|---|---|
| DDL → tab / clipboard | ✓ | ✓ | ✓ | ✓ | ✓ | – | ✓ | ✓ | ✓* |
| Top 100 / count rows | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Duplicates | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | – | ✓ |
| Deleted rows | ✓ | – | – | – | – | – | – | – | – |
| GRANT template | ✓ | – | – | – | – | – | – | – | – |
| Maintenance (GROOM/VACUUM/ANALYZE) | ✓ | ✓ | ✓ | – | – | – | ✓ | – | – |
| Statistics (ANALYZE/RUNSTATS) | ✓ | ✓ | ✓ | – | – | – | ✓ | – | – |
| Distribution chart / code | ✓ | – | – | – | – | – | – | – | – |
| Empty table | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | – |
| Recreate | ✓ | ✓ | – | – | – | – | – | – | – |
| Import data | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | – |
| Export data | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Comments | ✓ | ✓ | ✓ | ✓ | – | – | – | – | – |
| PK / UNIQUE templates | ✓ | ✓ | ✓ | ✓ | – | ✓ | – | ✓ | – |
| EXPLAIN | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | – | ✓ |
| Rename | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | – |
| Create table from (CTAS) | ✓ | ✓ | – | – | – | – | ✓ | ✓ | – |
| Drop object | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | – |
| Compare DDL | ✓ | ✓ | ✓ | ✓ | ✓ | – | ✓ | ✓ | ✓ |
| Find usages / impact | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Schema value search | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | – | ✓ |

`*` Excel DDL reads the materialized definition; write actions stay hidden.

## Known gaps

- **DuckDB** uses the PostgreSQL intelligence surface until a dedicated `SqlDialect.DuckDb` exists. Its driver exposes only tables and views; procedures, synonyms, sequences and index/trigger catalogs are hidden.
- **MySQL** is a stub. The connection and query helpers work, but the create-table template, procedures and synonyms are not implemented; DDL/comment/procedure/synonym actions are hidden. MySQL is included in the Slim build but must not be presented as feature-complete.
- **Oracle** index and trigger groups are browse-only: the driver does not generate DDL for existing indexes/triggers yet. Statistics/GROOM actions are hidden because `DBMS_STATS` is not wired.
- **PostgreSQL** has no synonym, aggregate, trigger or tablespace catalog in the driver; CTAS is hidden because the base template emits Netezza's `DISTRIBUTE ON RANDOM`.
- **SQLite** has no procedures, functions, sequences, synonyms or aggregates. `ALTER TABLE ... ADD [PRIMARY KEY|UNIQUE]` is not supported, so PK/UNIQUE templates are hidden.
- **Access** exposes tables and views only; comments, EXPLAIN, duplicates and schema value search are hidden.
- **Excel** is a read-only materialization. Drop, empty, import, rename and constraint actions are hidden.

### Definition index, impact and definition search

`SourceIndexService` reads every procedure, view and (where supported) external-table definition once per connection and keeps the text for 15 minutes. References are found with the dialect lexer instead of a plain text filter:

- `Parsed`: a qualified name that resolves to the target, or an unqualified name in a table position (`FROM`, `JOIN`, `INTO`, `CALL`, ...) of the same schema;
- `Likely`: the name appears as an identifier but may point at another object;
- `Text`: the name appears only inside a string literal or comment (including dynamic SQL).

The schema menu exposes **Find usages** (direct references) and **Impact analysis** (three transitive levels, depth clamped to 1-6 and nodes capped at 500) with line numbers and snippets. The Schema Search "Source on" toggle uses the same index, so it reports the matching definition line instead of re-reading cached source text on every keystroke. Run `source/JustyBase.Tests/SourceIndexTests.cs` for the lexer/confidence contract.

### Db2 sessions, copy and export

- **Sessions** (formerly "NZ Sessions") lists Netezza sessions or Db2 LUW connections from `SYSPROC.MON_GET_CONNECTION`. Termination uses `DROP SESSION` on Netezza and `CALL SYSPROC.ADMIN_CMD('force application ( n ) MODE ASYNC')` on Db2; the monitor refuses to terminate its own Db2 application handle.
- **Copy to another connection…** streams one table or view into a table on a different saved connection. Columns map by name, the target table can be created from portable type mappings, and values are bound as parameters; rows never travel through the UI.
- Result export adds **JSON**, **XML**, **TSV** and **SQL INSERT script** next to CSV/Parquet/Excel. **Export DDL + data…** writes the driver's own `CREATE TABLE` followed by streamed `INSERT` statements.

### Db2 lint rule ids

Avalonia and the Rust/Tauri edition previously reused `DB2001`–`DB2004` with different meanings. Avalonia now namespaces its Db2 LUW lint rules as `DB2L001`–`DB2L008` (select star, delete/update without WHERE, Netezza carry-overs, top-N, LIMIT, double-dot). Rust keeps `DB2001`–`DB2004` for its language diagnostics (LIMIT, ILIKE, TEXT, register parentheses). `Db2LintRulesTests` asserts the two namespaces never overlap.

SQLite support covers the native SQLite object model rather than a table-editor abstraction: databases and attached catalogs,
tables, views, columns (including primary-key/generated/hidden flags), indexes (unique/partial/expression/order/collation metadata),
triggers, foreign keys, schema DDL, read-only/immutable sessions, shared in-memory databases, and integrity/foreign-key diagnostics.
