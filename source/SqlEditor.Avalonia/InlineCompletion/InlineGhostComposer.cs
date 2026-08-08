namespace JustyBase.Editor.InlineCompletion;

/// <summary>
/// Builds the ghost text shown at the caret from a FIM suggestion and, when the
/// autocomplete list is open, the remainder of the selected completion item.
///
/// Mirrors VS Code's inline-completion "augmentation" rule
/// (see `singleTextEditAugments` in the VS Code repository): while a list item is
/// selected, the FIM tail is only rendered when it continues from the selected item
/// (the suggestion starts with the item's remaining text). A suggestion that does not
/// augment the selection is hidden entirely so the ghost never conflicts with the list.
/// </summary>
public static class InlineGhostComposer
{
    /// <summary>
    /// Composes the text to render as ghost text.
    /// </summary>
    /// <param name="suggestion">Raw FIM suggestion returned by the backend.</param>
    /// <param name="selectedSeed">
    /// Remainder of the selected completion item (its insert text minus what the user
    /// already typed), or an empty string when no item is selected.
    /// </param>
    /// <returns>
    /// The text to render and the number of leading characters already covered by the
    /// selected item (used to keep the FIM tail after the item is accepted). An empty
    /// <see cref="InlineGhostResult.Text"/> means nothing should be rendered.
    /// </returns>
    public static InlineGhostResult Compose(string suggestion, string selectedSeed) =>
        Compose(suggestion, selectedSeed, StringComparison.OrdinalIgnoreCase);

    public static InlineGhostResult Compose(
        string suggestion,
        string selectedSeed,
        StringComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        ArgumentNullException.ThrowIfNull(selectedSeed);

        if (selectedSeed.Length == 0)
        {
            return new InlineGhostResult(suggestion, 0);
        }

        // No tail beyond the selected item, or the suggestion does not continue from it —
        // hide it entirely so the ghost never duplicates/conflicts with the list.
        if (suggestion.Length <= selectedSeed.Length
            || !suggestion.StartsWith(selectedSeed, comparison))
        {
            return InlineGhostResult.None;
        }

        return new InlineGhostResult(
            selectedSeed + suggestion[selectedSeed.Length..],
            selectedSeed.Length);
    }
}

public readonly record struct InlineGhostResult(string Text, int PrefixLength)
{
    public static InlineGhostResult None { get; } = new(string.Empty, 0);
}