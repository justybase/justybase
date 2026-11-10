# No-AI build

JustyBase ships AI features as optional components. Set `EnableAi=false` to build
the application without the AI UI and without restoring the heavy AI NuGet
dependencies.

## What is excluded

The no-AI build removes the AI project references and source files:

- `JustyBase.Ai` and `JustyBase.Ai.Embedded`
- `GitHub.Copilot.SDK`, `Microsoft.Extensions.AI`,
  `Microsoft.Extensions.AI.OpenAI`, `OpenAI`, and embedded llama.cpp model
  management
- AI Chat, embedded FIM/chat, Codex/Copilot settings, and AI Chat tool windows
- AI-only services such as `Services/Ai`, `Services/Embedded`, `Services/Fim`,
  and the `AiChatViewModel` partials

The dependency-free `JustyBase.Ai.Contracts` project is still referenced. It
contains only chat models, host ports, and `SqlExecutionErrorStore`; it has no
NuGet dependencies. Test files that exercise AI-only types are also excluded
from `JustyBase.Tests` and `JustyBase.HeadlessTests` when `EnableAi=false`.

## Build

```powershell
# Whole solution (JustyBase.NoAi.slnx excludes the AI projects)
dotnet build JustyBase.NoAi.slnx -c Release -p:EnableAi=false

# Application only
dotnet build .\source\JustyBase\JustyBase.csproj -c Release -p:EnableAi=false

# Publish (example, Windows x64)
dotnet publish .\source\JustyBase\JustyBase.csproj -r win-x64 -c Release -f net11.0 `
  -p:EnableAi=false -p:PublishAot=true -p:PublishTrimmed=true --self-contained true
```

`EnableAi` defaults to `true`; omit the property for the standard AI-enabled
build. The `AI` preprocessor constant is defined only when `EnableAi=true`.

## Library-only no-AI build

The seven core libraries in the sibling `JustyBase.NetezzaSql` repository do not
reference the AI projects. Use the checked-in solution filter:

```powershell
dotnet build ..\JustyBase.NetezzaSql\JustyBase.NetezzaSql.NoAi.slnf -c Release
dotnet pack  ..\JustyBase.NetezzaSql\JustyBase.NetezzaSql.NoAi.slnf -c Release -o .\artifacts
```

The filter excludes `JustyBase.Ai`, `JustyBase.Ai.Embedded`,
`JustyBase.Ai.Contracts`, and the AI test/probe projects.

## Verification

After a no-AI build, confirm the output does not contain AI assemblies:

```powershell
Get-ChildItem .\source\JustyBase\bin\Release\net11.0 |
  Where-Object Name -Match 'JustyBase\.Ai(\.dll|\.Embedded)|Copilot|Microsoft\.Extensions\.AI|OpenAI'
```

Only `JustyBase.Ai.Contracts.dll` may appear. To inspect the restore graph:

```powershell
dotnet list .\source\JustyBase\JustyBase.csproj package --include-transitive |
  Select-String 'Copilot|Microsoft.Extensions.AI|OpenAI|JustyBase.Ai'
```

Run the check after a no-AI restore; the same command after a standard build
will show the AI packages.
