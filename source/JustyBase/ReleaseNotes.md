# JustyBase 1.0.0-rc.10

- Route Avalonia SQL completion through the shared completion orchestrator while preserving VS Code-style interaction with inline ghost text.
- Stabilize AI chat and completion review fixes, including the Avalonia chat view and completion navigation flow.
- Refresh runtime, AI, SQLite, and UI dependency versions used by the desktop application and tests.
- Release packaging disables the shared compiler (`UseSharedCompilation=false`) to avoid cross-platform artifact file locks during publish.
