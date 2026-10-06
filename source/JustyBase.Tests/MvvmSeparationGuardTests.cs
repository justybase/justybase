using System.Text.RegularExpressions;

namespace JustyBase.Tests;

/// <summary>
/// F0 guard: ViewModels must stay UI-free (F2 direction).
/// New code must not introduce Avalonia UI types into ViewModels; existing violations
/// are tracked via baselines so the count can only shrink.
/// Allowed in VMs: Dock.Model (docking abstractions), CommunityToolkit, System.
/// Disallowed: Avalonia.Controls (Control/Window/MenuItem/Bitmap), Dispatcher.UIThread
/// (use IUiDispatcher), concrete SqlCodeEditor (use IEditorAdapter).
/// </summary>
public sealed class MvvmSeparationGuardTests
{
    // Baseline at F0+F1+F2 introduction (measured 2026-10-02, includes broad regex hits
    // in comments/strings). Lower on every cleanup PR — never raise.
    // Note: Control regex is intentionally broad; real Avalonia Control usages in VMs are
    // ~12 (DbSchemaViewModel.MenuItems + FileExplorer columns), the rest is noise to squeeze out.
    private const int MaxAvaloniaUsingsInViewModels = 28;
    private const int MaxControlReferencesInViewModels = 109;
    private const int MaxDispatcherReferencesInViewModels = 21;

    private static readonly Regex AvaloniaUsing = new(
        @"^\s*using\s+Avalonia(\.|;)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex ControlType = new(
        @"\b(Control|Window|MenuItem|Bitmap|TextBlock)\b",
        RegexOptions.Compiled);

    private static readonly Regex DispatcherDirect = new(
        @"Dispatcher\.UIThread",
        RegexOptions.Compiled);

    [Fact]
    public void ViewModels_AvaloniaUsings_DoNotIncrease()
    {
        var root = TestSourceRoot.Find();
        var count = CountMatches(root, "JustyBase/ViewModels", AvaloniaUsing);
        Assert.True(
            count <= MaxAvaloniaUsingsInViewModels,
            $"Avalonia usings in ViewModels: {count} > baseline {MaxAvaloniaUsingsInViewModels}. "
            + "Use IUiDispatcher / IEditorAdapter / SchemaMenuItemViewModel instead.");
    }

    [Fact]
    public void ViewModels_UiControlReferences_DoNotIncrease()
    {
        var root = TestSourceRoot.Find();
        var count = CountMatches(root, "JustyBase/ViewModels", ControlType);
        Assert.True(
            count <= MaxControlReferencesInViewModels,
            $"UI Control references in ViewModels: {count} > baseline {MaxControlReferencesInViewModels}. "
            + "Use SchemaMenuItemViewModel + DataTemplate instead of Control/MenuItem.");
    }

    [Fact]
    public void ViewModels_DispatcherDirectReferences_DoNotIncrease()
    {
        var root = TestSourceRoot.Find();
        var count = CountMatches(root, "JustyBase/ViewModels", DispatcherDirect);
        Assert.True(
            count <= MaxDispatcherReferencesInViewModels,
            $"Dispatcher.UIThread in ViewModels: {count} > baseline {MaxDispatcherReferencesInViewModels}. "
            + "Use IUiDispatcher instead.");
    }

    [Fact]
    public void NewAbstractions_Exist()
    {
        var root = TestSourceRoot.Find();
        Assert.True(File.Exists(Path.Combine(root, "JustyBase", "Editor", "IEditorAdapter.cs")), "IEditorAdapter missing");
        Assert.True(File.Exists(Path.Combine(root, "JustyBase", "Services", "Ai", "IAiChatNavigator.cs")), "IAiChatNavigator missing");
        Assert.True(File.Exists(Path.Combine(root, "JustyBase", "Services", "Dialogs", "IQuickOpenDialogService.cs")), "IQuickOpenDialogService missing");
        Assert.True(File.Exists(Path.Combine(root, "JustyBase", "Services", "FileExplorer", "IFileIconProvider.cs")), "IFileIconProvider missing");
        Assert.True(File.Exists(Path.Combine(root, "JustyBase", "ViewModels", "Tools", "SchemaMenuItemViewModel.cs")), "SchemaMenuItemViewModel missing");
    }

    private static int CountMatches(string root, string relativeDir, Regex pattern)
    {
        var dir = Path.Combine(root, relativeDir.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(dir))
        {
            return 0;
        }

        var total = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            total += pattern.Matches(text).Count;
        }

        return total;
    }
}
