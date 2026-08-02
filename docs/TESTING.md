# Testing

## Layers

| Layer | Project | When |
|-------|---------|------|
| Unit | `source/JustyBase.Tests` | Every PR (`dotnet-test.yml`) |
| Headless UI smoke | `source/JustyBase.HeadlessTests` | Every PR |
| DB integration | `source/JustyBase.IntegrationTests` | Netezza workflow / secrets |
| Live FlaUI (optional) | `source/JustyBase.LiveTests` | Manual `workflow_dispatch` / local only |

## Local commands

```bash
# Unit + concurrency guards
dotnet test .\source\JustyBase.Tests\JustyBase.Tests.csproj -c Debug

# Avalonia headless (UI responsiveness budgets, dual SQL mock, smoke)
dotnet test .\source\JustyBase.HeadlessTests\JustyBase.HeadlessTests.csproj -c Debug

# Optional: custom path for responsiveness JSONL metrics
# $env:RESPONSIVENESS_METRICS_PATH = ".\TestResults\responsiveness-metrics.jsonl"

# Optional live FlaUI screenshots (Windows; starts JustyBase.exe)
dotnet build .\source\JustyBase\JustyBase.csproj -c Debug
# Nav / About / layout gallery
dotnet test .\source\JustyBase.LiveTests\JustyBase.LiveTests.csproj -c Debug --filter FullyQualifiedName~FlaUIScreenshotCaptureTests
# README hero: Schema Search refresh → JUST_DATA/DIMDATE → SELECT * → results screenshot
dotnet test .\source\JustyBase.LiveTests\JustyBase.LiveTests.csproj -c Debug --filter FullyQualifiedName~ReadmeSqlResultsHero
```

Hero scenario expects a configured connection that can run `SELECT * FROM JUST_DATA..DIMDATE`. Screenshots land in `pictures/live/` (and refresh `pictures/main.png`). UIA dump: `pictures/live/uia-tree*.txt`.

Each live test maximizes Justy Base, widens the left Schema/Schema Search panel (dock splitter drag clamped inside the window), and forces Justy Base to the foreground before input. It does **not** use the clipboard (clipboard changes can activate Google Keep), does **not** send global Ctrl+V, and refuses input unless the foreground window belongs to the Justy Base process. Live run logs under `pictures/live/*.txt` are gitignored.

Gallery `evo1.png` is the rich README scene (**Editor, schema and results**): advanced CTE SQL in the editor, SQL results grid, expanded Schema tree toward `JUST_DATA`/`DIMDATE`, refreshed Schema Search with an open column Name filter. Overwrites `evo1.png` only when results are present (requires NZ/`JUST_DATA`).

Optional: set `JUSTYBASE_EXE` to a custom build path.

## UI responsiveness (HeadlessTests)

Headless tests pump the Avalonia dispatcher while slow work runs (delayed `IDatabaseService` mocks or simulated FS delay) and assert:

| Metric | Budget |
|--------|--------|
| `MaxStallMs` (largest gap between dispatcher timer ticks) | ≤ 150 ms |
| `TickCount` | ≥ 5 |

Covered paths: Schema Search refresh, DbSchema connection expand, Import connection/database cascade, File Explorer tree expand.

Each run appends a JSONL line (also printed as `RESPONSIVENESS ...` in test output):

- Default file: `TestResults/responsiveness-metrics.jsonl` under the test output directory (`AppContext.BaseDirectory`)
- Override with env `RESPONSIVENESS_METRICS_PATH`

Fields: `test`, `operation`, `maxStallMs`, `tickCount`, `meanTickGapMs`, `elapsedMs`, `injectedDelayMs`, `timestamp`.

## Concurrency guards

`ConcurrencyGuardTests` scans `JustyBase` + `JustyBase.PluginBase` sources and fails on new:

- `GetAwaiter().GetResult()`
- `.Wait(` on tasks (allowlist: crash handler, emergency cleanup, `DatabaseCacheManager` bounded `WaitAny`)

Prefer `await` + [`UiThreadMarshal`](../source/JustyBase/Helpers/UiThreadMarshal.cs) or `ManualResetEventSlim` for off-UI → UI capture.

## Live / FlaUI (on demand)

Workflow: [`.github/workflows/live-ui-manual.yml`](../.github/workflows/live-ui-manual.yml)

- **Headless live**: runs HeadlessTests concurrency / schema scenarios (no FlaUI).
- **FlaUI**: Windows job builds `JustyBase.exe`, runs `JustyBase.LiveTests` (`Category=Live`), uploads `pictures/live` artifacts.

Do **not** enable FlaUI on every PR — keep it dispatch-only.
