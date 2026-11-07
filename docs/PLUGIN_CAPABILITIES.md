# Database plugin capabilities

JustyBase is **Netezza-first**. Other engines are available with uneven maturity. Use this matrix when evaluating multi-DB support.

| Engine | Maturity | Connect | Schema browse | Run SELECT | DDL / scripts helpers | Notes |
|--------|----------|---------|---------------|------------|------------------------|-------|
| **Netezza (dotnet)** | **stable** | Yes | Yes | Yes | Yes | Primary product path |
| **DB2** | **experimental** | Yes | Yes | Yes | Yes | Highly experimental: in-development managed DRDA driver (`JustyBase.Db2` 0.1.0, LUW-only). Do not use in production. External tables not implemented. Schema tree includes indexes, range partitions, triggers and tablespaces; connection options cover current schema, TLS and statement timeout. Definition index ("Find usages"/impact), session monitor (`FORCE APPLICATION`), cross-connection copy, JSON/XML/TSV/SQL export and DDL+data scripts are available; Db2 lint ids use the `DB2L` namespace |
| **Postgres** | **stable** | Yes | Yes | Yes | Yes | External tables / synonyms incomplete |
| **Oracle** | **experimental** | Yes | Yes | Yes | Partial | Some catalog queries still stubbed |
| **DuckDB** | **experimental** | Yes | Yes | Yes | Partial | Loaded as optional plugin DLL |
| **MySQL** | **stub** | Yes | Partial | Yes | No | Minimal surface; many DDL helpers throw |
| **SQLite** | **stable** | Yes | Yes | Yes | Yes | Native catalog browsing, indexes, triggers, foreign keys, ATTACH, SQLite DDL and diagnostics |
| **Excel File SQL** | **experimental** | Yes | Yes | Yes | Read-only materialization | `.xlsx` and `.xlsb` worksheets are exposed as DuckDB-backed tables; export supports separate `.xlsx`/`.xlsb` result tabs |
| **Microsoft Access** | **experimental** | Yes | Yes | Yes | Partial | `.mdb`/`.accdb` through `JustyBase.UCanAccessCs`; Access SQL editor dialect, read-only by default |

**Maturity legend**

- **stable** — suitable for day-to-day use of core IDE flows (connect, browse, run, common scripts)
- **experimental** — works for many flows; expect gaps and rough edges
- **stub** — connection / query smoke only; do not expect feature parity

SQLite, DuckDB, Excel File SQL and Microsoft Access may be loaded via the plugin directory rather than the default in-process registration list.

Schema context menus and tree groups are gated by `DatabaseCapabilities` (`JustyBase.PluginCommon/Models/DatabaseCapabilities.cs`). Actions the driver cannot express in valid SQL are hidden instead of producing statements the server rejects - for example, Db2 shows REORG/RUNSTATS where Netezza shows GROOM/GENERATE STATISTICS, offers alias/nickname DDL instead of synonyms, and hides distribution, deleted-row and grant actions until a Db2 implementation exists. Db2 also exposes its index, data-partition, trigger and tablespace groups, and the schema menu offers comment/drop/PK/UNIQUE/rename/CTAS/EXPLAIN/compare/find-usages helpers where the engine supports them.

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
