using JustyBase.Ai.Fim.Abstractions;

namespace JustyBase.Ai.Fim.Prompting;

/// <summary>DeepSeek-Coder FIM special tokens (alternative model family).</summary>
public sealed class DeepSeekFimPromptBuilder : IFimPromptBuilder
{
    public string ModelFamilyId => "deepseek-coder";

    public IReadOnlyList<string> StopSequences { get; } =
    [
        "<｜fim▁begin｜>",
        "<｜fim▁hole｜>",
        "<｜fim▁end｜>",
        "<|EOT|>",
        "<｜end▁of▁sentence｜>",
    ];

    public string Build(string prefix, string suffix) =>
        $"<｜fim▁begin｜>{prefix}<｜fim▁hole｜>{suffix}<｜fim▁end｜>";
}
