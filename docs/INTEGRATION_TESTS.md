# Netezza integration tests (optional)

`source/JustyBase.IntegrationTests` runs a single read-only check (`SELECT 1`) against a **real** Netezza instance using `JustyBase.NetezzaDriver`.

Default PR CI (`dotnet-test.yml`) does **not** run this project. Use local script or the manual GitHub Actions workflow `netezza-integration.yml` (`workflow_dispatch`).

Hosted GitHub runners usually cannot reach on-prem / VPN Netezza. Prefer:

- local `scripts/test-netezza-integration.ps1`, or
- a self-hosted runner that can route to the host

## When to run

- After driver or Netezza plugin changes that affect connectivity
- Before a release, if you have access to a dev/test Netezza host

## Configuration

Set these environment variables (never commit values):

| Variable | Description |
|----------|-------------|
| `NZ_DEV_HOST` | Hostname or IP |
| `NZ_DEV_DATABASE` | Database name |
| `NZ_DEV_USER` | User |
| `NZ_DEV_PASSWORD` | Password |
| `NZ_DEV_PORT` | TCP port |

## Run locally

```powershell
$env:NZ_DEV_HOST = "..."
$env:NZ_DEV_DATABASE = "..."
$env:NZ_DEV_USER = "..."
$env:NZ_DEV_PASSWORD = "..."
$env:NZ_DEV_PORT = "5480"

pwsh ./scripts/test-netezza-integration.ps1
```

Or:

```powershell
dotnet test source/JustyBase.IntegrationTests/JustyBase.IntegrationTests.csproj -c Release --filter "Category=Integration"
```

## GitHub Actions

1. Add repository secrets: `NZ_DEV_HOST`, `NZ_DEV_DATABASE`, `NZ_DEV_USER`, `NZ_DEV_PASSWORD`, `NZ_DEV_PORT`
2. Actions → **netezza-integration** → Run workflow

Tests are tagged `Category=Integration`. Contributors should keep them out of the default unit-test command.
