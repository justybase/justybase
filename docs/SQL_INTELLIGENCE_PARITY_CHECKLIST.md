# SQL intelligence parity checklist (Avalonia ↔ Lite)

Use the same NZ script in Avalonia and Lite. Packages: `JustyBase.NetezzaSqlParser` **0.2.0-preview.6** / `JustyBase.Netezza` **0.2.0-preview.5**.

## Automated

- [x] Package quick-fix matrix (`LintCodeActionTests`, `NzLintCodeActionsParityTests`)
- [x] Adapter + prefetch contract (`MetadataPrefetchAndAdapterParityTests`)
- [x] Avalonia completion merge policy (`SqlCompletionProviderLegacyPolicyTests`)
- [x] Avalonia Fix-all / InsertText / schema sync unit tests (see `SqlIntelligenceParityHostTests`)

## Manual (same script)

1. `SELECT * FROM sch.tbl` → NZ001; expand with schema when columns cached.
2. `DELETE FROM t;` / `UPDATE t SET a=1;` → NZ003/SQL044 / NZ002/SQL043; Fix / Fix-all safe.
3. `CREATE TABLE t AS SELECT 1;` → NZ011/SQL045; DISTRIBUTE ON RANDOM.
4. `UPDATE t AS x SET a=1` → NZ012/SQL046; remove AS.
5. NZPLSQL `ELSEIF` → NZP012 → `ELSIF`.
6. After connect (no manual Refresh): `FROM schema.` lists tables; `alias.` lists columns (lazy hydrate if ≥500 objects).
7. Diagnostics: **Fix all safe**; caret context menu on marker.
8. Settings → SQL Linter: Off / Warning / Error for NZ001, NZ002, NZ003, NZ011.

## Intentional gaps

- NZP021: not in Lite automatic Problems set — documented in `NzProcedureRules.cs`.
- Query-flow / CTE refactor, SAS macros, explain graph, VS Code Problems webview — out of scope.
