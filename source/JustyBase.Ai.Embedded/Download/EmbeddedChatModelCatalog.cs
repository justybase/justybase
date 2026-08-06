namespace JustyBase.Ai.Embedded.Download;

/// <summary>Well-known embedded chat model ids persisted in AppOptions.</summary>
public static class EmbeddedChatModelIds
{
    public const string Gemma4_12B = "gemma4-12b-it";
    public const string Qwen36_27B = "qwen3.6-27b";
    public const string Devstral2_22B = "devstral-2-22b";
    public const string Qwen36_35BA3B = "qwen3.6-35b-a3b";
    public const string Qwen35_9B = "qwen3.5-9b";
    public const string Qwen35_4B = "qwen3.5-4b";
    public const string Gemma4_31B = "gemma4-31b";
    public const string Gemma4_26BA4B = "gemma4-26b-a4b";

    public const string Default = Qwen35_4B;
}

/// <summary>
/// Catalog of instruct GGUF chat models served by the bundled llama.cpp llama-server for the
/// "Embedded" AI chat backend.
///
/// NOTE: exact HuggingFace repo/file names must be verified when these model releases are
/// published — the URLs below are best-effort (bartowski/QuantFactory naming convention).
/// </summary>
public sealed class EmbeddedChatModelCatalog : IModelCatalog
{
    public IReadOnlyList<ModelDescriptor> Models { get; } =
    [
        new(
            Id: EmbeddedChatModelIds.Qwen35_4B,
            DisplayName: "Qwen 3.5 4B (Q4_K_M) — default",
            FileName: "Qwen3.5-4B-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/bartowski/Qwen3.5-4B-GGUF/resolve/main/Qwen3.5-4B-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~2.7 GB",
            SourceModelUrl: new Uri("https://huggingface.co/Qwen/Qwen3.5-4B"),
            Notes: "Smallest chat model — best starting point on iGPU/CPU. Apache-2.0.",
            ApproxBytes: 2_700_000_000),
        new(
            Id: EmbeddedChatModelIds.Qwen35_9B,
            DisplayName: "Qwen 3.5 9B (Q4_K_M)",
            FileName: "Qwen3.5-9B-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/bartowski/Qwen3.5-9B-GGUF/resolve/main/Qwen3.5-9B-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~6 GB",
            SourceModelUrl: new Uri("https://huggingface.co/Qwen/Qwen3.5-9B"),
            Notes: "Balanced quality/speed — recommended when 8+ GB VRAM is available.",
            ApproxBytes: 6_000_000_000),
        new(
            Id: EmbeddedChatModelIds.Gemma4_12B,
            DisplayName: "Gemma 4 12B Instruct (gemma4:12b-it-q4_K_M)",
            FileName: "gemma-4-12b-it-q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/bartowski/Gemma-4-12B-it-GGUF/resolve/main/gemma-4-12b-it-q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~9 GB",
            SourceModelUrl: new Uri("https://huggingface.co/google/gemma-4-12b-it"),
            Notes: "Google Gemma 4 Instruct — subject to Gemma Terms of Use.",
            ApproxBytes: 9_000_000_000,
            Family: "Gemma 4",
            RequiresLicenseAcceptance: true,
            LicenseName: "Gemma Terms of Use",
            LicenseUrl: new Uri("https://ai.google.dev/gemma/terms"),
            LicenseSummary: "Gemma models are subject to Google's Gemma Terms of Use. You must review and accept those terms before downloading."),
        new(
            Id: EmbeddedChatModelIds.Devstral2_22B,
            DisplayName: "Devstral 2 22B (Q4_K_M)",
            FileName: "Devstral-2-22B-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/bartowski/Devstral-2-22B-GGUF/resolve/main/Devstral-2-22B-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~14 GB",
            SourceModelUrl: new Uri("https://huggingface.co/mistralai/Devstral-2-22B"),
            Notes: "Mistral dev-focused instruct model — license acceptance required.",
            ApproxBytes: 14_000_000_000,
            Family: "Devstral (Mistral)",
            RequiresLicenseAcceptance: true,
            LicenseName: "Mistral license",
            LicenseUrl: new Uri("https://mistral.ai/legal/"),
            LicenseSummary: "Devstral is released by Mistral AI under its model license. You must review and accept the license before downloading."),
        new(
            Id: EmbeddedChatModelIds.Qwen36_27B,
            DisplayName: "Qwen 3.6 27B (q4)",
            FileName: "Qwen3.6-27B-q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/bartowski/Qwen3.6-27B-GGUF/resolve/main/Qwen3.6-27B-q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~18 GB",
            SourceModelUrl: new Uri("https://huggingface.co/Qwen/Qwen3.6-27B"),
            Notes: "High-quality chat — needs 24+ GB VRAM or fast CPU + 32 GB RAM.",
            ApproxBytes: 18_000_000_000),
        new(
            Id: EmbeddedChatModelIds.Gemma4_26BA4B,
            DisplayName: "Gemma 4 26B-A4B (MoE, Q4_K_M)",
            FileName: "gemma-4-26b-a4b-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/bartowski/Gemma-4-26B-A4B-GGUF/resolve/main/gemma-4-26b-a4b-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~17 GB",
            SourceModelUrl: new Uri("https://huggingface.co/google/gemma-4-26b-a4b"),
            Notes: "MoE (4B active) — faster inference than dense 26B at similar quality. Gemma license.",
            ApproxBytes: 17_000_000_000,
            Family: "Gemma 4 (MoE)",
            RequiresLicenseAcceptance: true,
            LicenseName: "Gemma Terms of Use",
            LicenseUrl: new Uri("https://ai.google.dev/gemma/terms"),
            LicenseSummary: "Gemma models are subject to Google's Gemma Terms of Use. You must review and accept those terms before downloading."),
        new(
            Id: EmbeddedChatModelIds.Qwen36_35BA3B,
            DisplayName: "Qwen 3.6 35B-A3B (MoE, Q4_K_M)",
            FileName: "Qwen3.6-35B-A3B-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/bartowski/Qwen3.6-35B-A3B-GGUF/resolve/main/Qwen3.6-35B-A3B-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~20 GB",
            SourceModelUrl: new Uri("https://huggingface.co/Qwen/Qwen3.6-35B-A3B"),
            Notes: "MoE (3B active) — large capability at MoE inference cost.",
            ApproxBytes: 20_000_000_000,
            Family: "Qwen (MoE)"),
        new(
            Id: EmbeddedChatModelIds.Gemma4_31B,
            DisplayName: "Gemma 4 31B (Q4_K_M)",
            FileName: "gemma-4-31b-Q4_K_M.gguf",
            DownloadUri: new Uri("https://huggingface.co/bartowski/Gemma-4-31B-GGUF/resolve/main/gemma-4-31b-Q4_K_M.gguf?download=true"),
            ApproxSizeLabel: "~21 GB",
            SourceModelUrl: new Uri("https://huggingface.co/google/gemma-4-31b"),
            Notes: "Largest Gemma 4 dense — 32+ GB VRAM / heavy RAM required. Gemma license.",
            ApproxBytes: 21_000_000_000,
            Family: "Gemma 4",
            RequiresLicenseAcceptance: true,
            LicenseName: "Gemma Terms of Use",
            LicenseUrl: new Uri("https://ai.google.dev/gemma/terms"),
            LicenseSummary: "Gemma models are subject to Google's Gemma Terms of Use. You must review and accept those terms before downloading."),
    ];

    public ModelDescriptor Resolve(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return Models.First(m => m.Id == EmbeddedChatModelIds.Default);
        }

        foreach (var model in Models)
        {
            if (string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase))
            {
                return model;
            }
        }

        return Models.First(m => m.Id == EmbeddedChatModelIds.Default);
    }
}
