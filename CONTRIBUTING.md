# Contributing to JustyBase

Thank you for your interest in contributing to JustyBase! This document provides guidelines and instructions for contributing.

## Code of Conduct

Be respectful and inclusive. We welcome contributions from everyone.

## Getting Started

1. Fork the repository
2. Clone your fork locally
3. Create a feature branch
4. Make your changes
5. Submit a pull request

## Development Setup

### Prerequisites

- .NET 10.0 SDK
- IDE: Visual Studio 2022, Rider, or VS Code with C# extension
- Git

### Dependencies

The DataGrid and hierarchical schema tree are restored from the official `ProDataGrid` NuGet package, version `12.0.5`.

Builds require the sibling checkouts `../JustyBase.NetezzaSql` and `../JustyBase.UCanAccessCs`. Their projects are listed in `JustyBase.slnx` and used directly through project references; there is no NuGet fallback for these libraries. GitHub Actions workflow definitions check out both repositories next to `JustyBase` before restore/build.

### First Build

```bash
dotnet restore JustyBase.slnx
dotnet build JustyBase.slnx -c Debug
dotnet test source/JustyBase.Tests/JustyBase.Tests.csproj
```

### Running the Application

```bash
cd source/JustyBase
dotnet run
```

UI language is **English only**.

## Project Structure

```text
JustyBase/
├── source/
│   ├── JustyBase/              # Main Avalonia desktop app
│   ├── JustyBase.PluginCommon/ # Plugin contracts
│   ├── JustyBase.PluginBase/   # Base plugin implementation
│   ├── JustyBase.Common/       # Shared utilities
│   ├── SqlEditor.Avalonia/     # SQL editor component
│   ├── Plugins/                # Database provider plugins
│   └── JustyBase.Tests/        # Unit tests
└── docs/                       # Architecture and contributor docs
```

## Coding Standards

- File-scoped namespaces; PascalCase types/members; `_camelCase` private fields
- Prefer constructor injection over `App.GetRequiredService<T>()`
- Use source-generated `JsonSerializerContext` for JSON
- Keep Avalonia bindings compiled (`x:DataType` / `{CompiledBinding}`)
- Prefer focused methods; avoid new `#region` blocks

### Commit message format

```text
type(scope): subject
```

Types: `feat`, `fix`, `refactor`, `docs`, `test`, `chore`.

## Pull Request Process

1. Branch from `main` / `master` (`feat/...` or `fix/...`)
2. Add or update tests for behavior changes
3. Run unit + headless tests locally
4. Update docs when build/run steps change

### PR checklist

- [ ] Solution builds
- [ ] Unit tests pass
- [ ] Headless smoke tests pass when UI is touched
- [ ] Docs updated if needed

## Testing

```bash
dotnet test source/JustyBase.Tests/JustyBase.Tests.csproj
dotnet test source/JustyBase.HeadlessTests/JustyBase.HeadlessTests.csproj
```

Optional live Netezza integration: see [docs/INTEGRATION_TESTS.md](docs/INTEGRATION_TESTS.md).

## Architecture notes

- Plugins inherit `DatabaseService` and set `WHO_I_AM_CONST`
- Release artifacts are self-contained ReadyToRun (no NativeAOT, no trimming), so reflection-based providers and in-process plugins work as-is
- See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for layering and SQL run flow

## Questions?

Open a GitHub issue with the `question` label.
