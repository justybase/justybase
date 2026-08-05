# Database plugin capabilities

JustyBase is **Netezza-first**. Other engines are available with uneven maturity. Use this matrix when evaluating multi-DB support.

| Engine | Maturity | Connect | Schema browse | Run SELECT | DDL / scripts helpers | Notes |
|--------|----------|---------|---------------|------------|------------------------|-------|
| **Netezza (dotnet)** | **stable** | Yes | Yes | Yes | Yes | Primary product path |
| **DB2** | **stable** | Yes | Yes | Yes | Yes | External tables not implemented |
| **Postgres** | **stable** | Yes | Yes | Yes | Yes | External tables / synonyms incomplete |
| **Oracle** | **experimental** | Yes | Yes | Yes | Partial | Some catalog queries still stubbed |
| **DuckDB** | **experimental** | Yes | Yes | Yes | Partial | Loaded as optional plugin DLL |
| **MySQL** | **stub** | Yes | Partial | Yes | No | Minimal surface; many DDL helpers throw |
| **SQLite** | **stub** | Yes | Yes | Yes | No | Useful for local smoke; DDL helpers empty |

**Maturity legend**

- **stable** — suitable for day-to-day use of core IDE flows (connect, browse, run, common scripts)
- **experimental** — works for many flows; expect gaps and rough edges
- **stub** — connection / query smoke only; do not expect feature parity

SQLite and DuckDB may be loaded via the plugin directory rather than the default in-process registration list.
