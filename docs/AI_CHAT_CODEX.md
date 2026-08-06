# Codex in AI Chat

JustyBase connects to a ChatGPT/Codex subscription through the official Codex app-server. It does not use an OpenAI API key and it does not copy Codex credentials into the JustyBase credential store.

## Setup

1. Install the Codex CLI and make sure `codex` is available on `PATH`.
2. Enable `AI Chat` in Preferences.
3. Select `Sign in with ChatGPT` in the AI Chat settings or in the chat header.
   JustyBase restores the existing Codex account after restart and refreshes the
   account status while the browser login is completing.
4. Select `Codex (ChatGPT)` as the provider.

If the executable is not on `PATH`, set `JUSTYBASE_CODEX_COMMAND` to its full path before starting JustyBase. The app starts it as `codex app-server --stdio` and keeps its state under `%AppData%\JustDataEvo\codex`.

When Codex is selected, the model list also exposes the reasoning-effort options advertised by `model/list`; the selected value is sent as the `effort` override for each turn.

## Safety boundary

Codex receives the active SQL document and metadata tools only. JustyBase starts it with an app-owned `CODEX_HOME`, a read-only empty workspace, and unrelated shell/filesystem/web/app tools disabled. Result-grid rows and arbitrary file reads are not exposed. `execute_sql` and document changes require a visible approval card in the chat. The same tool policy is used by the OpenAI Compatible and Embedded backends.

The `execute_sql` tool intentionally uses a non-query execution path and returns status/error information only; it never sends result rows back to the model.

## Data-flow and privacy details

The AI features have different trust boundaries. Enabling one feature does not automatically enable the others.

| Feature | Where inference runs | Data that can leave the machine | Authentication / storage |
|---|---|---|---|
| Embedded FIM | Local llama.cpp `llama-server` subprocess | The model download request and model metadata go to the configured Hugging Face source; prompts are evaluated locally after download | No provider account; model files are stored under `%LOCALAPPDATA%/JustyBase/models/` |
| Embedded (chat) | Local llama.cpp `llama-server` subprocess (chat model) | Same as FIM — model download from Hugging Face, prompts evaluated locally | No provider account; GGUF stored under `%LOCALAPPDATA%/JustyBase/models/` |
| OpenAI Compatible | The user's configured endpoint (LM Studio, Ollama `/v1`, llama.cpp, vLLM, …) | Requests go to the endpoint configured by the user, normally `localhost` | Managed by the user's local service; optional API key kept in the app's protected data store |
| Codex (ChatGPT) | The official Codex app-server process | Chat messages, the active SQL context and selected schema metadata may be sent to the Codex/ChatGPT service through the official CLI | Codex owns authentication; JustyBase keeps only non-secret account/thread state in its own settings |

### What the chat tools can access

The application exposes a deliberately narrow tool surface to the model:

- the current SQL text and selected editor context,
- SQL diagnostics,
- selected connection/database identifiers,
- schema browsing and metadata lookup,
- a controlled SQL execution operation,
- an explicit document-change operation for proposed SQL fixes.

The model is not given a general filesystem tool, arbitrary workspace file access, result-grid row access, shell execution, or browser access. The Codex process is started with an application-owned `CODEX_HOME`, an empty read-only workspace, and unrelated Codex tools disabled.

### Approval and destructive operations

`execute_sql` and document-changing operations are approval-gated. A generated SQL statement is shown in the chat approval card before it can be applied. Rejecting the card prevents the operation. A provider response by itself never grants permission to modify a document or execute SQL.

### Local storage and sensitive data

Database credentials are stored by the application in its protected local data store. Chat history, settings, Codex thread identifiers, diagnostic logs, and downloaded models may still contain sensitive information depending on how the application is used. Do not commit the application data directory, diagnostic logs, GGUF files, or exported SQL containing production data.

Before enabling a remote provider, verify that sending SQL text and schema names is permitted by your organization's policy. For confidential work, prefer Embedded FIM or a locally hosted OpenAI-compatible service, while remembering that local services have their own logging and retention settings.

### Recommended user checklist

1. Keep AI Chat disabled when it is not needed.
2. Review the active provider and endpoint before sending a prompt.
3. Remove secrets, customer data, and production identifiers from SQL examples.
4. Review every SQL execution or document-change approval card.
5. Keep local provider logs and downloaded models outside the Git repository.
6. Use the clear-data controls and delete model files when the workstation is shared or retired.

## Troubleshooting

- `Codex CLI was not found`: install the CLI or set `JUSTYBASE_CODEX_COMMAND`.
- `Sign in with ChatGPT` opens a browser: finish the login there and wait for the
  header status to change to the signed-in account. If it remains stale, click
  `Sign in` once more to refresh the account state.
- Legacy Ollama / LM Studio configurations are migrated to the single **OpenAI Compatible**
  backend (`http://localhost:11434/v1` or `http://localhost:1234/v1` respectively).
