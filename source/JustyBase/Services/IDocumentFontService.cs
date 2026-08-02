namespace JustyBase.Services;

public interface IDocumentFontService
{
    IReadOnlyList<FontFamily> GetAvailableFonts();
    FontFamily? GetFontByName(string? fontName);
}
