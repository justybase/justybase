using System.ComponentModel;
using JustyBase.Services;
using JustyBase.ViewModels.Documents;

namespace JustyBase.Views.Documents;

public partial class SettingsView : UserControl
{
    private readonly IAvaloniaSpecificHelpers _avaloniaSpecificHelpers;
    private readonly IDocumentFontService _documentFontService;
    private SettingsViewModel? _subscribedViewModel;
    private bool _applyingSearchFilter;
    private string _currentPageId = "General";

    private static readonly string[] ContentPanelIds =
    [
        "General",
        "Export",
        "SnipettsANDkeywords",
        "SqlLinter",
        "EmbeddedAi",
        "EmbeddedAiChat",
        "AiChat",
        "Results",
        "Apperance",
        "Others",
    ];

    private static readonly Dictionary<string, string> TagToPageId = new(StringComparer.OrdinalIgnoreCase)
    {
        ["General"] = "General",
        ["Export"] = "Export",
        ["SnipettsANDkeywords"] = "SnipettsANDkeywords",
        ["SqlLinter"] = "SqlLinter",
        ["EmbeddedAi"] = "EmbeddedAi",
        ["EmbeddedAiChat"] = "EmbeddedAiChat",
        ["AiChat"] = "AiChat",
        ["Results"] = "Results",
        ["Limits"] = "Results",
        ["Apperance"] = "Apperance",
        ["Others"] = "Others",
    };

    public SettingsView(IAvaloniaSpecificHelpers avaloniaSpecificHelpers, IDocumentFontService documentFontService)
    {
        _avaloniaSpecificHelpers = avaloniaSpecificHelpers;
        _documentFontService = documentFontService;
        InitializeComponent();
        treeView.SelectionChanged += TreeView_SelectionChanged;
        btEditSnippets.Click += BtEditSnippets_Click;
        btOk.Click += BtOk_Click;

        fontDropDown.ItemsSource = _documentFontService.GetAvailableFonts();
        this.DataContextChanged += SettingsView_DataContextChanged;
        fontDropDown.SelectionChanged += FontDropDown_SelectionChanged;
        this.AttachedToVisualTree += (_, _) => EnsureDefaultSelection();
    }

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    private void FontDropDown_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        ViewModel.DocumentFontName = (fontDropDown.SelectedItem as FontFamily)?.Name ?? "Cascadia Code";
    }

    private void SettingsView_DataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _subscribedViewModel = null;
        }

        if (ViewModel is null)
        {
            return;
        }

        _subscribedViewModel = ViewModel;
        _subscribedViewModel.PropertyChanged += ViewModel_PropertyChanged;

        if (fontDropDown.ItemsSource is not null)
        {
            foreach (var font in fontDropDown.ItemsSource.OfType<FontFamily>())
            {
                if (font.Name == ViewModel.DocumentFontName)
                {
                    fontDropDown.SelectedItem = font;
                    break;
                }
            }
        }

        ApplySearchFilter(selectFirstMatch: false);
        EnsureDefaultSelection();
    }

    private void EnsureDefaultSelection()
    {
        if (treeView.SelectedItem is not null)
        {
            ShowPage(_currentPageId);
            return;
        }

        var general = FindTreeViewItemByTag(treeView.Items.OfType<TreeViewItem>(), "General");
        if (general is not null)
        {
            treeView.SelectedItem = general;
        }

        ShowPage("General");
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsViewModel.SearchText)
            or nameof(SettingsViewModel.MatchingSectionIds))
        {
            ApplySearchFilter(selectFirstMatch: true);
        }
    }

    private void ApplySearchFilter(bool selectFirstMatch)
    {
        if (ViewModel is null || _applyingSearchFilter)
        {
            return;
        }

        _applyingSearchFilter = true;
        try
        {
            var hasQuery = !string.IsNullOrWhiteSpace(ViewModel.SearchText);
            var matchingSet = ViewModel.MatchingSectionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var item in EnumerateTreeViewItems(treeView.Items.OfType<TreeViewItem>()))
            {
                var tag = item.Tag?.ToString();
                if (string.IsNullOrEmpty(tag))
                {
                    item.IsVisible = true;
                    continue;
                }

                var selfMatch = !hasQuery || matchingSet.Contains(tag);
                var childMatch = item.Items.OfType<TreeViewItem>()
                    .Any(child =>
                    {
                        var childTag = child.Tag?.ToString();
                        return !string.IsNullOrEmpty(childTag) && matchingSet.Contains(childTag);
                    });

                item.IsVisible = selfMatch || childMatch;
                if (hasQuery && childMatch)
                {
                    item.IsExpanded = true;
                }
            }

            if (hasQuery && selectFirstMatch)
            {
                var firstId = ViewModel.FirstMatchingSectionId;
                if (firstId is not null)
                {
                    var treeItem = FindTreeViewItemByTag(treeView.Items.OfType<TreeViewItem>(), firstId);
                    if (treeItem is not null)
                    {
                        treeView.SelectedItem = treeItem;
                    }

                    ShowPage(ResolvePageId(firstId));
                    return;
                }
            }

            ShowPage(_currentPageId);
        }
        finally
        {
            _applyingSearchFilter = false;
        }
    }

    private void ShowPage(string pageId)
    {
        var resolved = ResolvePageId(pageId);
        _currentPageId = resolved;

        foreach (var id in ContentPanelIds)
        {
            var panel = this.FindControl<Control>(id);
            if (panel is not null)
            {
                panel.IsVisible = string.Equals(id, resolved, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static string ResolvePageId(string tagOrPageId)
    {
        if (TagToPageId.TryGetValue(tagOrPageId, out var pageId))
        {
            return pageId;
        }

        return tagOrPageId;
    }

    private static IEnumerable<TreeViewItem> EnumerateTreeViewItems(IEnumerable<TreeViewItem> roots)
    {
        foreach (var item in roots)
        {
            yield return item;
            foreach (var child in EnumerateTreeViewItems(item.Items.OfType<TreeViewItem>()))
            {
                yield return child;
            }
        }
    }

    private static TreeViewItem? FindTreeViewItemByTag(IEnumerable<TreeViewItem> roots, string tag)
    {
        foreach (var item in EnumerateTreeViewItems(roots))
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    private static Flyout? _savedFlyout;

    private void BtOk_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _savedFlyout ??= new Flyout
        {
            Content = new TextBlock
            {
                Text = $"Saved{Environment.NewLine}you can now close this tab"
            },
            FlyoutPresenterClasses =
            {
                "GoodVsibleFlyout"
            }
        };

        _savedFlyout.ShowAt(btOk);
    }

    private async void BtEditSnippets_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var sn = new SnippetWindow();
        await sn.ShowDialog(_avaloniaSpecificHelpers.GetMainWindow());
    }

    private void TreeView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_applyingSearchFilter)
        {
            return;
        }

        var en = treeView.SelectedItems.GetEnumerator();
        en.MoveNext();
        if (en.Current is TreeViewItem viewItem)
        {
            var tag = viewItem.Tag?.ToString();
            if (!string.IsNullOrEmpty(tag))
            {
                ShowPage(tag);
            }
        }
    }
}
