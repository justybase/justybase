namespace JustyBase.Services;

public sealed class DocumentFontService : IDocumentFontService
{
    private readonly IReadOnlyList<FontFamily> _availableFonts;

    public DocumentFontService()
        : this(GetRuntimeFonts())
    {
    }

    public DocumentFontService(IEnumerable<FontFamily> fonts)
    {
        _availableFonts = BuildUniqueByName(fonts);
    }

    public IReadOnlyList<FontFamily> GetAvailableFonts()
    {
        return _availableFonts;
    }

    public FontFamily? GetFontByName(string? fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName))
        {
            return null;
        }

        return _availableFonts.FirstOrDefault(
            font => string.Equals(font.Name, fontName, StringComparison.OrdinalIgnoreCase));
    }

    private static List<FontFamily> BuildUniqueByName(IEnumerable<FontFamily> fonts)
    {
        return fonts
            .GroupBy(static font => font.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static groupedFonts => groupedFonts.First())
            .ToList();
    }

    private static List<FontFamily> GetRuntimeFonts()
    {
        var availableFonts = new List<FontFamily>();
        if (App.Current?.Resources["JetBrainsMono"] is FontFamily customFont)
        {
            availableFonts.Add(customFont);
        }

        availableFonts.AddRange(FontManager.Current.SystemFonts);
        return availableFonts;
    }
}
