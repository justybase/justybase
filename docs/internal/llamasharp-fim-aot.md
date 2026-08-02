# LLamaSharp embedded FIM — Native AOT notes

Date probed: 2026-07-31  
Packages: `LLamaSharp` + `LLamaSharp.Backend.Cpu` **0.27.0**  
One-off probe project (`source/probes/LlamaSharpAotProbe`) was removed after the check; conclusions below stay valid.

## Summary

| Path | Result |
|------|--------|
| `dotnet run` (JIT) | **OK** — native CPU backend loads; AVX/AVX2 features reported |
| `dotnet publish -p:PublishAot=true -r win-x64` | **Compiles** with IL trim/AOT warnings |
| AOT published EXE runtime | **PROBE_OK** — `llama_print_system_info` works |

**Verdict:** publish succeeds, but LLamaSharp is **not fully AOT-clean**. Embedded FIM still ships in AOT builds; treat CPU/AVX backend selection as **best-effort under AOT**. Users who do not want FIM leave it disabled in Preferences.

## Observed AOT warnings (publish)

- `IL3053` / `IL2104` — LLamaSharp assembly produces AOT/trim analysis warnings
- `IL3000` — `Assembly.Location` is empty in single-file/AOT; native library path discovery is affected
- ILC note: `NativeApi.llama_backend_free()` “will always throw” (Invalid IL / metadata) — avoid relying on explicit backend free in AOT builds

## Runtime difference under AOT

JIT probe reported full CPU feature flags (SSE3/AVX/AVX2/…).  
AOT probe reported a reduced set (`BMI2`, `LLAMAFILE`, `OPENMP`, `REPACK`) — likely because AVX-variant DLL selection uses `Assembly.Location`-based search.

Inference may still run, but **GPU/AVX backend selection can be wrong or CPU-suboptimal** under Native AOT.

## Product policy (JustyBase)

MSBuild flags in `JustyBase.csproj`:

- `EnableEmbeddedFim` defaults to **`true`** for **all** configurations (Debug, Release, AOT)
- Opt-out of the *feature* is Preferences only (`EnableEmbeddedFimAi`, default **false**) — no download/inference until the user enables it and prepares a model
- Rare strip-from-binary escape hatch: `-p:EnableEmbeddedFim=false`
- Optional CUDA: build FIM library with `-p:EnableFimCuda=true` (adds `LLamaSharp.Backend.Cuda12`)

User settings (Preferences → **Embedded AI (FIM)**):

- `AppOptions.EnableEmbeddedFimAi` (default **false**) — opt-in ghost text
- `AppOptions.EmbeddedFimModelId` — `qwen2.5-coder-3b` or `qwen2.5-coder-7b`
- `AppOptions.EmbeddedFimDebounceSeconds` — idle delay before a suggestion (1–15, default **3**)
- `AppOptions.EmbeddedFimMaxTokens` — max suggestion length (20–200, default **50**)
- `AppOptions.EmbeddedFimContextWindow` — `Small` / `Medium` / `Large` document prefix+suffix window

GGUF files live under `%LOCALAPPDATA%/JustyBase/models/` after **Download / prepare**.

Full user-facing guide: [../EMBEDDED_FIM.md](../EMBEDDED_FIM.md).

Model choice:

| Id | Base card | GGUF download |
|----|-----------|---------------|
| `qwen2.5-coder-3b` (default) | [Qwen2.5-Coder-3B](https://huggingface.co/Qwen/Qwen2.5-Coder-3B) | bartowski `Qwen2.5-Coder-3B-Q4_K_M.gguf` (~1.9 GB) |
| `qwen2.5-coder-7b` | [Qwen2.5-Coder-7B](https://huggingface.co/Qwen/Qwen2.5-Coder-7B) | official `qwen2.5-coder-7b-q4_k_m.gguf` (~4.7 GB) |

Do **not** ship the large GGUF files inside the installer.  
`HuggingFaceFimModelStore` downloads on demand with progress callbacks.
