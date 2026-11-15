using Avalonia.Themes.Fluent;
using System.Text.Json.Serialization;

namespace JustyBase.Themes;

public sealed class PaletteDTO
{
    public bool HasResources { get; set; }
    public ColorInfo? Accent { get; set; }
    public ColorInfo? AltHigh { get; set; }
    public ColorInfo? AltLow { get; set; }
    public ColorInfo? AltMedium { get; set; }
    public ColorInfo? AltMediumHigh { get; set; }
    public ColorInfo? AltMediumLow { get; set; }
    public ColorInfo? BaseHigh { get; set; }
    public ColorInfo? BaseLow { get; set; }
    public ColorInfo? BaseMedium { get; set; }
    public ColorInfo? BaseMediumHigh { get; set; }
    public ColorInfo? BaseMediumLow { get; set; }
    public ColorInfo? ChromeAltLow { get; set; }
    public ColorInfo? ChromeBlackHigh { get; set; }
    public ColorInfo? ChromeBlackLow { get; set; }
    public ColorInfo? ChromeBlackMedium { get; set; }
    public ColorInfo? ChromeBlackMediumLow { get; set; }
    public ColorInfo? ChromeDisabledHigh { get; set; }
    public ColorInfo? ChromeDisabledLow { get; set; }
    public ColorInfo? ChromeGray { get; set; }
    public ColorInfo? ChromeHigh { get; set; }
    public ColorInfo? ChromeLow { get; set; }
    public ColorInfo? ChromeMedium { get; set; }
    public ColorInfo? ChromeMediumLow { get; set; }
    public ColorInfo? ChromeWhite { get; set; }
    public ColorInfo? ErrorText { get; set; }
    public ColorInfo? ListLow { get; set; }
    public ColorInfo? ListMedium { get; set; }
    public ColorInfo? RegionColor { get; set; }

    public ColorPaletteResources GetColorPaletteResources()
    {
        var palette = new ColorPaletteResources();
        Set(Accent, color => palette.Accent = color);
        Set(AltHigh, color => palette.AltHigh = color);
        Set(AltLow, color => palette.AltLow = color);
        Set(AltMedium, color => palette.AltMedium = color);
        Set(AltMediumHigh, color => palette.AltMediumHigh = color);
        Set(AltMediumLow, color => palette.AltMediumLow = color);
        Set(BaseHigh, color => palette.BaseHigh = color);
        Set(BaseLow, color => palette.BaseLow = color);
        Set(BaseMedium, color => palette.BaseMedium = color);
        Set(BaseMediumHigh, color => palette.BaseMediumHigh = color);
        Set(BaseMediumLow, color => palette.BaseMediumLow = color);
        Set(ChromeBlackHigh, color => palette.ChromeBlackHigh = color);
        Set(ChromeBlackLow, color => palette.ChromeBlackLow = color);
        Set(ChromeBlackMedium, color => palette.ChromeBlackMedium = color);
        Set(ChromeBlackMediumLow, color => palette.ChromeBlackMediumLow = color);
        Set(ChromeDisabledHigh, color => palette.ChromeDisabledHigh = color);
        Set(ChromeDisabledLow, color => palette.ChromeDisabledLow = color);
        Set(ChromeGray, color => palette.ChromeGray = color);
        Set(ChromeHigh, color => palette.ChromeHigh = color);
        Set(ChromeLow, color => palette.ChromeLow = color);
        Set(ChromeMedium, color => palette.ChromeMedium = color);
        Set(ChromeMediumLow, color => palette.ChromeMediumLow = color);
        Set(ChromeWhite, color => palette.ChromeWhite = color);
        Set(ListLow, color => palette.ListLow = color);
        Set(ListMedium, color => palette.ListMedium = color);
        Set(RegionColor, color => palette.RegionColor = color);
        return palette;
    }

    private static void Set(ColorInfo? info, Action<Color> apply)
    {
        if (info is not null)
        {
            apply(new Color(info.A, info.R, info.G, info.B));
        }
    }

}
[JsonSerializable(typeof(PaletteDTO))]
public partial class MyJsonContextPaletteDTO : JsonSerializerContext
{
}

[JsonSerializable(typeof(ColorPaletteResources))]
public partial class MyJsonContextColorPaletteResources : JsonSerializerContext
{
}




public sealed class ColorInfo
{
    public byte A { get; set; }
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
}

[JsonSerializable(typeof(ColorInfo))]
public partial class MyJsonContextColorInfo : JsonSerializerContext
{
}