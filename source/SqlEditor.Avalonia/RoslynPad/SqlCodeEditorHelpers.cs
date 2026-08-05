using JustyBase.PluginCommon.Contracts;

namespace JustyBase.Editor;

public static class SqlCodeEditorHelpers
{
    public const int BRACKET_SEARCH_LEN = 1024 * 128;
    public const char leftBracket = '(';
    public const char rightBracket = ')';
    public const int TypoLimit = 1;
    public static Dictionary<string, Dictionary<string, HighlightingBrush>> SqlColors { get; set; } = new()
    {
        {".sql|Light" ,
            new()
            {
                { "Keywords", new SimpleHighlightingBrush(Colors.Blue) },
                { "Char", new SimpleHighlightingBrush(Colors.Red) },
                { "NumberLiteral", new SimpleHighlightingBrush(Colors.Brown) },

                {"Comment",new SimpleHighlightingBrush(Colors.Green) },
                {"Preprocessor",new SimpleHighlightingBrush(Colors.Green) },

                {"MethodCall",new SimpleHighlightingBrush(Color.FromRgb(250,0,250)) },
                {"ValueTypeKeywords", new SimpleHighlightingBrush(Colors.BlueViolet) },
                //{"Parametr", new SimpleHighlightingBrush(Color.FromRgb(255, 0, 0)) },
                {"TrueFalse", new SimpleHighlightingBrush(Colors.DarkCyan) },
            }
        },
        {".sql|Dark" ,
            new()
            {
                { "Keywords", new SimpleHighlightingBrush(Colors.LightGreen) },
                { "Char", new SimpleHighlightingBrush(Colors.OrangeRed) },
                { "NumberLiteral", new SimpleHighlightingBrush(Colors.Orange) },

                {"Comment",new SimpleHighlightingBrush(Colors.Yellow) },
                {"Preprocessor",new SimpleHighlightingBrush(Colors.Yellow) },

                {"MethodCall",new SimpleHighlightingBrush(Color.FromRgb(250,0,250)) },
                {"ValueTypeKeywords", new SimpleHighlightingBrush(Colors.BlueViolet) },
               // {"Parametr", new SimpleHighlightingBrush(Color.FromRgb(0, 255, 0)) },
                {"TrueFalse", new SimpleHighlightingBrush(Colors.DarkCyan) },
            }
        }
    };
    public static SqlCodeEditor? LastFocusedEditor { get; set; }
    public static void ResetStyle(bool dark, string language = ".sql")
    {
        string keyName = $"{language}|{(dark ? "Dark" : "Light")}";

        if (SqlColors.TryGetValue(keyName, out Dictionary<string, HighlightingBrush>? tmpValue))
        {
            var syntax = HighlightingManager.Instance.GetDefinition(ISomeEditorOptions.REGISTERED_EXTENSIONS[language].name);
            if (syntax is not null)
            {
                foreach (var (key, val) in tmpValue)
                {
                    syntax.GetNamedColor(key).Foreground = val;
                }
            }
        }
    }

}
