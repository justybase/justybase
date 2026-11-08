using JustyBase.Common.Contracts;
using JustyBase.Helpers;
using JustyBase.Services.History;
using JustyBase.Views.OtherDialogs;

namespace JustyBase.Services.Dialogs;

/// <summary>Opens the Local History dialog for one file. Keeps window creation out of the document view model.</summary>
public interface ILocalHistoryDialogService
{
    Task ShowAsync(
        string filePath,
        Action<string>? onRestored = null,
        Func<string?>? getCurrentText = null,
        CancellationToken cancellationToken = default);
}

public sealed class LocalHistoryDialogService : ILocalHistoryDialogService
{
    private readonly ILocalHistoryService _history;
    private readonly IMessageForUserTools _messages;
    private readonly IAvaloniaSpecificHelpers _avaloniaSpecificHelpers;

    public LocalHistoryDialogService(
        ILocalHistoryService history,
        IMessageForUserTools messages,
        IAvaloniaSpecificHelpers avaloniaSpecificHelpers)
    {
        _history = history;
        _messages = messages;
        _avaloniaSpecificHelpers = avaloniaSpecificHelpers;
    }

    public async Task ShowAsync(
        string filePath,
        Action<string>? onRestored = null,
        Func<string?>? getCurrentText = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        var window = new LocalHistoryWindow(
            _history,
            _messages,
            filePath,
            onRestored,
            getCurrentText);
        var owner = _avaloniaSpecificHelpers.GetMainWindow();
        if (owner is null)
        {
            window.Show();
            return;
        }

        await window.ShowDialog(owner);
    }
}
