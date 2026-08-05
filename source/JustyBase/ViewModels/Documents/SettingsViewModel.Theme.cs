using Avalonia.Themes.Fluent;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustyBase.Common.Contracts;
using JustyBase.Editor;
using JustyBase.Themes;
using System.Text.Json;

namespace JustyBase.ViewModels.Documents;

partial class SettingsViewModel
{
    partial void InitializeTheme()
    {
        GetColors();
        _dispatcherTimer.Tick += DispatcherTimer_Tick;
        _dispatcherTimer?.Stop();
    }

    private void ApplyThemeMode(bool isDark)
    {
        FluentThemeManager.StaticFluentThemeManager?.Switch(isDark ? 1 : 0);
        SqlCodeEditorHelpers.ResetStyle(isDark);
        if (isDark)
        {
            SemanticLineColorizer.SetColors(
                comment: new SolidColorBrush(Colors.Yellow),
                str: new SolidColorBrush(Colors.OrangeRed),
                number: new SolidColorBrush(Colors.Orange),
                keyword: new SolidColorBrush(Colors.LightGreen),
                type: new SolidColorBrush(Colors.BlueViolet),
                function: new SolidColorBrush(Color.FromRgb(250, 0, 250)),
                variable: new SolidColorBrush(Color.FromRgb(0, 200, 200)),
                table: new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                column: new SolidColorBrush(Color.FromRgb(135, 206, 250)),
                cte: new SolidColorBrush(Color.FromRgb(255, 165, 0)),
                alias: new SolidColorBrush(Color.FromRgb(144, 238, 144)),
                identifier: new SolidColorBrush(Color.FromRgb(180, 180, 180)));
        }
        else
        {
            SemanticLineColorizer.SetColors(
                comment: new SolidColorBrush(Colors.Green),
                str: new SolidColorBrush(Colors.Red),
                number: new SolidColorBrush(Colors.Brown),
                keyword: new SolidColorBrush(Colors.Blue),
                type: new SolidColorBrush(Colors.BlueViolet),
                function: new SolidColorBrush(Color.FromRgb(250, 0, 250)),
                variable: new SolidColorBrush(Color.FromRgb(163, 4, 199)),
                table: new SolidColorBrush(Color.FromRgb(160, 82, 45)),
                column: new SolidColorBrush(Color.FromRgb(0, 100, 180)),
                cte: new SolidColorBrush(Color.FromRgb(184, 92, 0)),
                alias: new SolidColorBrush(Color.FromRgb(0, 128, 0)),
                identifier: new SolidColorBrush(Color.FromRgb(120, 120, 120)));
        }
    }

    private readonly DispatcherTimer _dispatcherTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2)
    };

    private void DispatcherTimer_Tick(object? sender, EventArgs e)
    {
        _dispatcherTimer?.Stop();
        SetColor();
    }

    [ObservableProperty]
    public partial double ControlContentThemeFontSize { get; set; }
    [ObservableProperty]
    public partial double CompletitionFontSize { get; set; }
    [ObservableProperty]
    public partial double DefaultFontSizeForDocuments { get; set; }

    private void StartColorTimer()
    {
        _dispatcherTimer?.Stop();
        _dispatcherTimer?.Start();
    }

    public Color Accent
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color AltHigh
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color AltLow
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color AltMedium
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color AltMediumLow
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color AltMediumHigh
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color BaseHigh
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color BaseLow
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color BaseMedium
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color BaseMediumHigh
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color BaseMediumLow
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color ChromeHigh
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();

        }
    }

    public Color ChromeLow
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color ChromeMedium
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color ChromeMediumLow
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color ChromeWhite
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color ChromeGray
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color ChromeBlackHigh
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color ChromeBlackLow
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color ChromeBlackMedium
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color ChromeBlackMediumLow
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color ChromeDisabledHigh
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color ChromeDisabledLow
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color ListLow
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color ListMedium
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    public Color RegionColor
    {
        get;
        set
        {
            SetProperty(ref field, value);
            StartColorTimer();
        }
    }

    private ColorPaletteResources GetSelectedPalett()
    {
        ColorPaletteResources pal = new()
        {
            Accent = Accent,
            AltHigh = AltHigh,
            AltLow = AltLow,
            AltMedium = AltMedium,
            AltMediumHigh = AltMediumHigh,
            AltMediumLow = AltMediumLow,
            BaseHigh = BaseHigh,
            BaseLow = BaseLow,
            BaseMedium = BaseMedium,
            BaseMediumHigh = BaseMediumHigh,
            BaseMediumLow = BaseMediumLow,

            ChromeBlackHigh = ChromeBlackHigh,
            ChromeBlackLow = ChromeBlackLow,
            ChromeBlackMedium = ChromeBlackMedium,
            ChromeBlackMediumLow = ChromeBlackMediumLow,

            ChromeDisabledHigh = ChromeDisabledHigh,
            ChromeDisabledLow = ChromeDisabledLow,
            ChromeGray = ChromeGray,

            ChromeHigh = ChromeHigh,
            ChromeLow = ChromeLow,
            ChromeMedium = ChromeMedium,
            ChromeMediumLow = ChromeMediumLow,

            ChromeWhite = ChromeWhite,

            ListLow = ListLow,
            ListMedium = ListMedium,
            RegionColor = RegionColor
        };
        return pal;
    }

    [RelayCommand]
    private void SerializeSelectedPalett()
    {
        ColorPaletteResources pal = GetSelectedPalett();

        var txt = JsonSerializer.Serialize(pal, MyJsonContextColorPaletteResources.Default.ColorPaletteResources);
        File.WriteAllText(IGeneralApplicationData.ColorsPath, txt);
        _messageForUserTools.ShowSimpleMessageBoxInstance("please restart application");
    }

    [RelayCommand]
    private void BackToDefaults()
    {
        try
        {
            File.Delete(IGeneralApplicationData.ColorsPath);
        }
        catch (Exception ex)
        {
            _messageForUserTools.ShowSimpleMessageBoxInstance(ex);
        }
        _messageForUserTools.ShowSimpleMessageBoxInstance("please restart application");
    }

    [RelayCommand]
    private void ChangeFontSize(object parametr)
    {
        if (parametr.ToString() == "+")
        {
            FluentThemeManager.MakeFontsBigger();
        }
        else if (parametr.ToString() == "-")
        {
            FluentThemeManager.MakeFontsSmaller();
        }
        else
        {
            FluentThemeManager.MakeFontsDefault();
        }
        _messageForUserTools.ShowSimpleMessageBoxInstance("please restart application");
    }

    private void SetColor()
    {
        ColorPaletteResources pal = GetSelectedPalett();
        FluentThemeManager.StaticFluentThemeManager.Switch(-1, pal);
    }

    private void GetColors()
    {
        var pal = JustyBase.Themes.FluentThemeManager.GetCurrentPalette();


        Accent = pal.Accent;
        AltHigh = pal.AltHigh;
        AltLow = pal.AltLow;
        AltMedium = pal.AltMedium;
        AltMediumHigh = pal.AltMediumHigh;
        AltMediumLow = pal.AltMediumLow;
        BaseHigh = pal.BaseHigh;
        BaseLow = pal.BaseLow;
        BaseMedium = pal.BaseMedium;
        BaseMediumHigh = pal.BaseMediumHigh;
        BaseMediumLow = pal.BaseMediumLow;

        ChromeBlackHigh = pal.ChromeBlackHigh;
        ChromeBlackLow = pal.ChromeBlackLow;
        ChromeBlackMedium = pal.ChromeBlackMedium;
        ChromeBlackMediumLow = pal.ChromeBlackMediumLow;

        ChromeDisabledHigh = pal.ChromeDisabledHigh;
        ChromeDisabledLow = pal.ChromeDisabledLow;
        ChromeGray = pal.ChromeGray;

        ChromeHigh = pal.ChromeHigh;
        ChromeLow = pal.ChromeLow;
        ChromeMedium = pal.ChromeMedium;
        ChromeMediumLow = pal.ChromeMediumLow;

        ChromeWhite = pal.ChromeWhite;

        ListLow = pal.ListLow;
        ListMedium = pal.ListMedium;
        RegionColor = pal.RegionColor;
    }
}
