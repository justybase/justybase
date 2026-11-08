using JustyBase.Common.Contracts;
using JustyBase.Services.Actions;
using JustyBase.Views.OtherDialogs;

namespace JustyBase.Services.Dialogs;

/// <summary>Shows Find Action and runs the chosen action against the active document.</summary>
public interface IActionPaletteService
{
    Task ShowAsync(CancellationToken cancellationToken = default);
}

public sealed class ActionPaletteService : IActionPaletteService
{
    private readonly ActionRegistry _registry;
    private readonly IActiveDocumentManager _activeDocuments;
    private readonly IAvaloniaSpecificHelpers _avaloniaSpecificHelpers;
    private readonly IMessageForUserTools _messages;

    public ActionPaletteService(
        ActionRegistry registry,
        IActiveDocumentManager activeDocuments,
        IAvaloniaSpecificHelpers avaloniaSpecificHelpers,
        IMessageForUserTools messages)
    {
        _registry = registry;
        _activeDocuments = activeDocuments;
        _avaloniaSpecificHelpers = avaloniaSpecificHelpers;
        _messages = messages;
    }

    public async Task ShowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IdeAction? chosen = null;
        var window = new ActionPaletteWindow(_registry, action => chosen = action);
        var owner = _avaloniaSpecificHelpers.GetMainWindow();
        if (owner is null)
        {
            return;
        }

        await window.ShowDialog(owner);

        if (chosen is null)
        {
            return;
        }

        var context = new ActionContext(_activeDocuments.ActiveSqlDocumentViewModel);
        try
        {
            bool ran = await _registry.RunAsync(chosen.Id, context, cancellationToken);
            if (!ran)
            {
                _messages.ShowSimpleMessageBoxInstance(
                    $"\"{chosen.Title}\" cannot run right now. Open a SQL document and try again.",
                    "Find Action");
            }
        }
        catch (OperationCanceledException)
        {
            // The caller cancelled; nothing to report.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _messages.ShowSimpleMessageBoxInstance(ex);
        }
    }
}
