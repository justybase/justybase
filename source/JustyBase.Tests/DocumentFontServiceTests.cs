using Avalonia.Media;
using JustyBase.Services;
using System.Linq;

namespace JustyBase.Tests;

public sealed class DocumentFontServiceTests
{
    [Fact]
    public void Constructor_DeduplicatesFontsByName_CaseInsensitive()
    {
        var service = new DocumentFontService(
        [
            new FontFamily("Consolas"),
            new FontFamily("consolas"),
            new FontFamily("Arial"),
        ]);

        var names = service.GetAvailableFonts().Select(static font => font.Name).ToList();

        Assert.Equal(2, names.Count);
        Assert.Contains("Consolas", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Arial", names, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetFontByName_ReturnsMatchingFont_WhenNameMatchesIgnoringCase()
    {
        var service = new DocumentFontService(
        [
            new FontFamily("JetBrains Mono"),
            new FontFamily("Arial"),
        ]);

        var result = service.GetFontByName("jetbrains mono");

        Assert.NotNull(result);
        Assert.Equal("JetBrains Mono", result!.Name);
    }

    [Fact]
    public void GetFontByName_ReturnsNull_WhenInputIsEmpty()
    {
        var service = new DocumentFontService(
        [
            new FontFamily("Consolas"),
        ]);

        var result = service.GetFontByName("");

        Assert.Null(result);
    }

    [Fact]
    public void GetFontByName_ReturnsNull_WhenFontIsMissing()
    {
        var service = new DocumentFontService(
        [
            new FontFamily("Consolas"),
            new FontFamily("Arial"),
        ]);

        var result = service.GetFontByName("NotExistingFont");

        Assert.Null(result);
    }
}
