# Embedded AI — Fill-in-the-Middle (FIM)

JustyBase can suggest SQL **inline** (gray ghost text) using a **local** GGUF model served by the
**bundled llama.cpp `llama-server`** subprocess — no cloud API key, works offline after the model
is downloaded. The same FIM server is also used (in **plain completion** mode, not FIM tokens) to
draft **Git commit messages** from the working-tree summary.

## Why it matters

- Completions respect **prefix and suffix** (Fill-in-the-Middle), so suggestions fit mid-statement edits, not only end-of-line autocomplete.
- Runs **locally** on your machine; SQL never leaves the workstation for this feature.
- **Tab** accepts the suggestion; **Esc** dismisses it.
- Tunable idle delay so heavy local inference does not interrupt fast typing.
- Git panel **sparkles** button can propose a commit subject/body from staged (preferred) or unstaged changes — no cloud call.

## How to use

### SQL ghost text (FIM)

1. Open **Preferences → Embedded AI (FIM)**.
2. Turn on **Enable Fill-in-the-Middle**.
3. Pick a **preset** and/or a model.
4. Click **Download / prepare** and wait until the status shows the model on disk / server ready.
5. Open a SQL document, pause typing for the configured delay — ghost text appears at the caret.

The first request also downloads the `llama-server.exe` binary (CPU `avx2` or `vulkan` variant)
into `%LOCALAPPDATA%\JustyBase\llama-server\` and starts a dedicated FIM server on a free
`127.0.0.1` port (separate from the embedded chat server — FIM and chat use different models).

### Git commit message

1. Enable Embedded FIM as above (model downloaded and ready).
2. Open the **Git** tool, select a repository with staged or unstaged changes.
3. Click the **sparkles** button next to the commit message box.
4. Review/edit the suggestion, then commit as usual.

The button is hidden when Embedded FIM is disabled. Generation prefers staged diff context when present.

| Preference | Meaning |
|------------|---------|
| Enable Fill-in-the-Middle | Master switch for SQL editor ghost text |
| Suggestion delay | Delay after you stop typing (default **600 ms**) |
| Preset | **Small** / **Medium** (default) / **Large** / **Custom** — sets prompt budget, prefix/suffix split, max generation tokens, and recommended model |
| Fine-tune | Max prompt tokens, prefix %, suffix %, max generation tokens (20–200). Editing any knob marks preset **Custom** |
| Prefer Vulkan GPU | Download the **vulkan** llama-server binary (AMD/Intel iGPU). Off = CPU (`avx2`) build. Reload the model after changing |
| GPU layers | `gpu_layers` offload (0 = CPU, **99** = max). Reloads model on change |
| Model | Catalog of Qwen / CodeGemma / StarCoder2 / Codestral GGUFs. License-gated models prompt for acceptance once |
| Download / prepare | Ensures GGUF under `%LOCALAPPDATA%\JustyBase\models\` and starts the FIM server |
| Speed test | End-to-end ghost-text latency + approx tokens/s |
| Delete model | Stops the FIM server and removes the selected GGUF from disk |

### Presets

| Preset | Prompt tokens | Prefix / suffix | Max gen | Default model |
|--------|---------------|-----------------|---------|---------------|
| Small | 512 | 60% / 40% | 30 | `qwen2.5-coder-1.5b` |
| Medium | 1536 | 65% / 35% | 50 | `qwen2.5-coder-3b` |
| Large | 4096 | 70% / 30% | 80 | `qwen2.5-coder-7b` (pick 14B manually if VRAM allows) |

Char windows ≈ `maxPromptTokens × 4 × percentage`.

### License acceptance

CodeGemma and Codestral require an explicit confirmation (license summary + URL) before the model stays selected. Accepted ids are stored in `FimAcceptedLicenseModelIds` and are not re-prompted.

Persisted options: `EnableFimServer`, `FimModelId`, `FimDebounceMs`, `FimMaxTokens`, `FimPreset`, `FimMaxPromptTokens`, `FimPrefixPercentage`, `FimSuffixPercentage`, `LlamaServerPreferVulkan`, `FimGpuLayers`, `FimCtxSize`, `FimAcceptedLicenseModelIds`.

## Models

Base **Qwen2.5-Coder** checkpoints (not Instruct) are recommended defaults; alternatives are optional:

| Id | Notes | Approx. download |
|----|-------|------------------|
| `qwen2.5-coder-1.5b` | Small preset | ~1.0 GB |
| `qwen2.5-coder-3b` | Catalog default / Medium fallback | ~1.9 GB |
| `qwen2.5-coder-7b` | Medium/Large recommended | ~4.7 GB |
| `qwen2.5-coder-14b` | Heavy upgrade | ~9.0 GB |
| `codegemma-2b` / `7b` | Gemma ToS acceptance required | ~1.6 / ~5.0 GB |
| `starcoder2-3b` / `7b` / `15b` | BigCode FIM | ~1.8 / ~4.4 / ~9.1 GB |
| `codestral-22b` | MNPL acceptance required | ~13 GB |

Models are **not** shipped in the installer; download on demand. llama.cpp has built-in FIM
templates for all of these families (`/completion` with `input_prefix` / `input_suffix`).

## Architecture (short)

| Piece | Role |
|-------|------|
| `JustyBase.Ai.Embedded` | `ICompletionProvider`, presets, Hugging Face store, `LlamaServerBinaryManager` / `LlamaServerInstance` / `LlamaServerManager`, `LlamaServerFimProvider` |
| `JustyBase.Ai.Embedded.Download` | FIM + embedded-chat GGUF catalogs and resumable downloader |
| `SqlEditor.Avalonia` / `InlineCompletion` | Debounced FIM ghost text + Tab accept |
| `LlamaServerGitCommitMessageAiService` | Plain (non-FIM) completion for commit messages via the FIM server |
| Git tool / Diff document | Status, stage/commit, history; side-by-side Diff tab (`CanFloat` off) |
| Preferences UI | Enable, delay, presets / fine-tune, model, license gate, download/delete |

The engine is a separate native process (`llama-server.exe`), so there is **no AOT impact** on the
JustyBase binary itself.

## Tips

- First suggestion after prepare may take longer (model load + server start).
- Increase **Suggestion delay** on slower CPUs; decrease it when you want snappier hints.
- Prefer **Small** on CPU-only machines; **Medium** on Vulkan iGPU; **Large** when you have a discrete GPU.
- Use **Delete model** to free disk space when switching sizes or uninstalling the feature.
