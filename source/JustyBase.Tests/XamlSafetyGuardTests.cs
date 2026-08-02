using System.Text.RegularExpressions;

namespace JustyBase.Tests;

/// <summary>
/// Static XAML guards that catch Avalonia crash classes before runtime:
/// control-as-Setter-value, StaticResource for runtime-only keys, Color used as Brush in Setters.
/// </summary>
public sealed class XamlSafetyGuardTests
{
    private static readonly string[] RuntimeOnlyResourceKeys =
    [
        "ControlContentThemeFontSize",
        "CompletitionFontSize",
        "DockApplicationAccentBrushLow",
        "DockApplicationAccentBrushMed",
        "DockApplicationAccentBrushHigh",
        "DockApplicationAccentBrushIndicator"
    ];

    private static readonly Regex ControlAsSetterValue = new(
        @"Setter\s+Property=""[^""]*(ContextMenu|Flyout|ToolTip|Popup)""\s+Value=""\{(Static|Dynamic)Resource",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ColorAsBrushSetter = new(
        @"Setter\s+Property=""(Background|Foreground|BorderBrush|Fill)""\s+Value=""\{(Static|Dynamic)Resource\s+System(Accent|Chrome|Alt|Base|Region)[^""]*Color\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void Axaml_ShouldNotAssignControlResourcesViaContextMenuSetters()
    {
        var violations = ScanAxamlFiles(ControlAsSetterValue);
        Assert.True(
            violations.Count == 0,
            "ContextMenu/Flyout/ToolTip/Popup must not be assigned via Setter Value={Static|DynamicResource}. " +
            "Override Dock's DocumentTabStripItemContextMenu resource instead.\n" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Axaml_ShouldNotUseStaticResourceForRuntimeOnlyKeys()
    {
        var pattern = new Regex(
            @"\{StaticResource\s+(" + string.Join("|", RuntimeOnlyResourceKeys) + @")\}",
            RegexOptions.Compiled);

        var violations = ScanAxamlFiles(pattern);
        Assert.True(
            violations.Count == 0,
            "Runtime-updated resource keys must use DynamicResource (and XAML fallbacks), not StaticResource.\n" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Axaml_ShouldNotUseFluentColorKeysAsBrushInSetters()
    {
        var violations = ScanAxamlFiles(ColorAsBrushSetter);
        Assert.True(
            violations.Count == 0,
            "Use *Brush resources (e.g. SystemAccentColorBrush) for Background/Foreground/BorderBrush/Fill Setters, not Color keys.\n" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void AppAxaml_ShouldDeclareFontSizeFallbacks()
    {
        var appAxaml = FindSourceFile(Path.Combine("source", "JustyBase", "App.axaml"));
        var text = File.ReadAllText(appAxaml);

        Assert.Contains("""x:Key="ControlContentThemeFontSize""", text, StringComparison.Ordinal);
        Assert.Contains("""x:Key="CompletitionFontSize""", text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            """DocumentContextMenu" Value="{""",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentTabContextMenu_ShouldOverrideDockResourceKey()
    {
        var menuAxaml = FindSourceFile(Path.Combine("source", "JustyBase", "Themes", "SqlDocumentTabContextMenu.axaml"));
        var text = File.ReadAllText(menuAxaml);

        Assert.Contains("""x:Key="DocumentTabStripItemContextMenu""", text, StringComparison.Ordinal);
        Assert.DoesNotContain("""x:Key="SqlDocumentTabContextMenu""", text, StringComparison.Ordinal);
    }

    private static List<string> ScanAxamlFiles(Regex pattern)
    {
        var root = FindRepoRoot();
        var justyBaseDir = Path.Combine(root, "source", "JustyBase");
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(justyBaseDir, "*.axaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("<!--", StringComparison.Ordinal) || IsCommentedLine(lines, i))
                {
                    // Cheap skip for fully-commented lines; multi-line comments are rare for Setters.
                    if (line.TrimStart().StartsWith("<!--", StringComparison.Ordinal) ||
                        line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                if (pattern.IsMatch(line))
                {
                    var relative = Path.GetRelativePath(root, file);
                    violations.Add($"{relative}:{i + 1}: {line.Trim()}");
                }
            }
        }

        return violations;
    }

    private static bool IsCommentedLine(string[] lines, int index)
    {
        var trimmed = lines[index].TrimStart();
        return trimmed.StartsWith("<!--", StringComparison.Ordinal) ||
               trimmed.StartsWith("*", StringComparison.Ordinal); // inside block comment leftovers
    }

    private static string FindSourceFile(string relativePath)
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Could not locate {relativePath}");
        }

        return path;
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "source", "JustyBase", "JustyBase.csproj");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate JustyBase repository root from test runtime directory.");
    }
}
