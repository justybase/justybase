using JustyBase.Editor.InlineCompletion;

namespace JustyBase.Tests;

public sealed class InlineGhostComposerTests
{
    [Fact]
    public void AugmentingSuggestion_KeepsSeedAndTail()
    {
        var result = InlineGhostComposer.Compose(
            "CALENDARSEMESTER, CALENDARYEAR",
            "CALENDARSEMESTER");

        Assert.Equal("CALENDARSEMESTER, CALENDARYEAR", result.Text);
        Assert.Equal("CALENDARSEMESTER".Length, result.PrefixLength);
    }

    [Fact]
    public void NonAugmentingSuggestion_IsDropped()
    {
        // FIM text that does not continue from the selected item must not conflict with the list.
        var result = InlineGhostComposer.Compose("AND D.STATUS = 'A'", "CALENDARSEMESTER");

        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(0, result.PrefixLength);
    }

    [Fact]
    public void SuggestionEqualToSeed_IsDropped()
    {
        // No tail beyond the selected item -> nothing extra to render (no standalone preview).
        var result = InlineGhostComposer.Compose("CALENDARSEMESTER", "CALENDARSEMESTER");

        Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public void EmptySeed_KeepsWholeSuggestion()
    {
        // List closed (or the item fully typed): the whole FIM text is the continuation.
        var result = InlineGhostComposer.Compose("AND DATEDIFF(...)", string.Empty);

        Assert.Equal("AND DATEDIFF(...)", result.Text);
        Assert.Equal(0, result.PrefixLength);
    }

    [Fact]
    public void CaseInsensitiveMatch_IsAccepted()
    {
        // SQL identifiers are case-insensitive; treat augmentation as case-insensitive too.
        var result = InlineGhostComposer.Compose(
            "calendarSemester, calendarYear",
            "CALENDARSEMESTER");

        Assert.Equal("CALENDARSEMESTER, calendarYear", result.Text);
    }

    [Fact]
    public void CaseSensitiveComparison_RejectsCaseOnlyDifference()
    {
        var result = InlineGhostComposer.Compose(
            "calendarSemester, X",
            "CALENDARSEMESTER",
            StringComparison.Ordinal);

        Assert.Equal(string.Empty, result.Text);
    }
}