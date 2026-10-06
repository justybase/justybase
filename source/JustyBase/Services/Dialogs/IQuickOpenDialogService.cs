using JustyBase.QuickOpen;

namespace JustyBase.Services.Dialogs;

/// <summary>
/// F1: Dialog service for Quick Open (Ctrl+P).
/// Replaces <c>new QuickOpenWindow(vm); ShowDialog(owner)</c> embedded in
/// <c>MainWindowViewModel.ShowQuickOpenAsync</c> so the VM stays testable
/// without Avalonia <c>Window</c>. The service owns both the VM lifetime
/// and the window lifetime; the caller only supplies data.
/// </summary>
public interface IQuickOpenDialogService
{
    /// <summary>
    /// Shows Quick Open over the given candidates and returns the accepted hit (null = cancelled).
    /// Handles ":line" navigation via <paramref name="gotoLine"/> internally.
    /// </summary>
    Task<QuickOpenHit?> ShowAsync(
        IReadOnlyList<QuickOpenCandidate> candidates,
        string? initialQuery,
        Action<int>? gotoLine,
        CancellationToken cancellationToken = default);
}
