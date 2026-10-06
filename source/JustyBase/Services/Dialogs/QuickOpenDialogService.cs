using JustyBase.QuickOpen;
using JustyBase.Services;
using JustyBase.ViewModels;
using JustyBase.Views.OtherDialogs;

namespace JustyBase.Services.Dialogs;

/// <summary>
/// F1: Avalonia implementation of Quick Open dialog.
/// Owns <c>QuickOpenWindow</c> + <c>QuickOpenViewModel</c> lifetime;
/// the caller (MainWindowViewModel) only supplies candidates.
/// </summary>
public sealed class QuickOpenDialogService : IQuickOpenDialogService
{
    private readonly IAvaloniaSpecificHelpers _windows;
    private readonly QuickOpenSearchService _searchService;

    public QuickOpenDialogService(IAvaloniaSpecificHelpers windows)
        : this(windows, new QuickOpenSearchService())
    {
    }

    internal QuickOpenDialogService(IAvaloniaSpecificHelpers windows, QuickOpenSearchService searchService)
    {
        _windows = windows;
        _searchService = searchService;
    }

    public async Task<QuickOpenHit?> ShowAsync(
        IReadOnlyList<QuickOpenCandidate> candidates,
        string? initialQuery,
        Action<int>? gotoLine,
        CancellationToken cancellationToken = default)
    {
        QuickOpenHit? accepted = null;
        bool completed = false;
        QuickOpenWindow? dialog = null;

        var vm = new QuickOpenViewModel(
            _searchService,
            candidates,
            TimeSpan.FromSeconds(10),
            closeCancel: () =>
            {
                if (completed)
                {
                    return;
                }

                completed = true;
                dialog?.Close(null);
            },
            closeAccept: hit =>
            {
                if (completed)
                {
                    return;
                }

                completed = true;
                accepted = hit;
                if (dialog is not null)
                {
                    dialog.IsAccepting = true;
                    dialog.Close(hit);
                }
            },
            initialQuery: initialQuery,
            gotoLine: gotoLine);

        var owner = _windows.GetMainWindow();
        if (owner is null)
        {
            return null;
        }

        dialog = new QuickOpenWindow(vm);
        var result = await dialog.ShowDialog<QuickOpenHit?>(owner).ConfigureAwait(false);
        return accepted ?? result;
    }
}
