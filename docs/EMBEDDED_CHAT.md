# Embedded AI — Chat (local llama-server)

The **Embedded (local)** AI chat backend runs a bundled llama.cpp `llama-server` subprocess with a
downloaded GGUF **chat** model. It is a third provider option in AI Chat, next to
**Codex/ChatGPT** and **OpenAI Compatible**.

## Key facts

- Separate server instance from FIM (different port, different model): FIM keeps a small
  FIM-capable coder model, chat can use a larger instruct model. Both models are resident when
  both features are enabled.
- The server exposes an OpenAI-compatible `/chat/completions` endpoint on `127.0.0.1`, so the
  entire existing chat pipeline works unchanged — including the **agent / tool-calling loop**
  (schema tools, SQL Fix, approval cards).
- Offline after the model is downloaded. No API key required.

## Setup

1. Open **Preferences → Embedded AI (Chat)**.
2. Turn on **Enable Embedded Chat**.
3. Pick a model and click **Download / prepare** (Gemma 4 / Devstral 2 require license acceptance).
4. In **Preferences → AI Chat** set **Default backend = Embedded (local)** (or switch in the chat header).
5. The first switch downloads the `llama-server.exe` binary (vulkan or cpu variant) and starts the server.

## Model catalog (download list)

> **Source policy:** the official model provider's own Q4 GGUF is used when available (Google
> Gemma QAT q4_0); otherwise the trusted unsloth Q4_K_M community builds are used.

| Id | Model | Approx. size | Source |
|----|-------|--------------|--------|
| `qwen3.5-4b` | Qwen 3.5 4B (Q4_K_M) | ~2.7 GB | unsloth — **default** |
| `qwen3.5-9b` | Qwen 3.5 9B (Q4_K_M) | ~5.7 GB | unsloth |
| `gemma4-12b-it` | Gemma 4 12B Instruct (QAT q4_0) | ~8.5 GB | Google official. Gemma Terms required |
| `devstral-2-22b` | Devstral Small 2 24B Instruct (Q4_K_M) | ~14 GB | unsloth. Mistral license required |
| `qwen3.6-27b` | Qwen 3.6 27B (Q4_K_M) | ~18 GB | unsloth |
| `gemma4-26b-a4b` | Gemma 4 26B-A4B (MoE, QAT q4_0) | ~16 GB | Google official. Gemma license |
| `qwen3.6-35b-a3b` | Qwen 3.6 35B-A3B (MoE, Q4_K_M) | ~20 GB | unsloth (UD dynamic) |
| `gemma4-31b` | Gemma 4 31B (QAT q4_0) | ~21 GB | Google official. Gemma license |

> **Note:** large models (24B–35B) require substantial VRAM/RAM — prefer the small models for the
> agent (tool-calling) modes.

## Agent / tool calling

llama.cpp `llama-server` implements OpenAI-style function calling. `LocalChatService` runs the
tool loop for local backends in-process:

1. The model receives the tool schemas and the prompt.
2. If it returns a `tool_calls` delta, the tool executes (write tools still require the approval card).
3. The result is appended as a `tool` message and the next round is generated (max 5 rounds).

This applies to all three chat modes: **Expert** (schema tools), **SQL Fix** (diagnostics +
apply), **Simple** (no tools).

## Settings (Preferences)

| Preference | Meaning |
|------------|---------|
| Enable Embedded Chat | Offer the Embedded backend in AI Chat (default off) |
| Model | Chat GGUF catalog (above) with license gates |
| GPU layers | `gpu_layers` offload for the chat server (0 = CPU, 99 = max) |
| Context size | Token context window (default 4096) |
| Download / prepare | Downloads GGUF to `%LOCALAPPDATA%\JustyBase\models\` |
| Show in folder / Delete model | Manage the local GGUF |

Shared with FIM: **Prefer Vulkan GPU** chooses the `vulkan` vs `cpu` llama-server binary.

Persisted options: `EnableEmbeddedChatAi`, `EmbeddedChatModelId`, `EmbeddedChatGpuLayers`,
`EmbeddedChatCtxSize`, `EmbeddedChatAcceptedLicenseModelIds`, `LlamaServerPreferVulkan`.

## Notes

- The chat and FIM servers are started on demand and stopped on app exit.
- The engine is an external native process, so it has no impact on the JustyBase managed process.
- Backend selection in Preferences migrates legacy `ollama` / `lmstudio` values to
  `openai-compatible` with the matching default endpoint.
