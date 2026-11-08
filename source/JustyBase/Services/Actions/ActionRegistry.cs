namespace JustyBase.Services.Actions;

/// <summary>What an action runs against: the document that was active when it was asked for, the
/// main window for workspace-level commands, and an optional parameter a menu or a gesture supplied.</summary>
public sealed record ActionContext(
    ViewModels.Documents.SqlDocumentViewModel? Document = null,
    object? Parameter = null,
    ViewModels.MainWindowViewModel? Main = null);

/// <summary>
/// One action declared once by the feature that owns it. The same declaration feeds the command
/// palette, menus, shortcuts, tooltips and (later) agents; nothing else needs to know how an action
/// is invoked. <see cref="CanRun"/> answers whether the action makes sense in a context and, when it
/// is false, <see cref="WhyNot"/> should say why in words a person can read.
/// </summary>
public sealed record IdeAction(
    string Id,
    string Title,
    string Group,
    Func<ActionContext, Task> Run,
    Func<ActionContext, bool>? CanRun = null,
    string? Shortcut = null,
    Func<ActionContext, string?>? WhyNot = null)
{
    public bool CanExecute(ActionContext context) => GetWhyNot(context) is null;

    /// <summary>Null when the action can run; otherwise the reason a person should see.</summary>
    public string? GetWhyNot(ActionContext context)
    {
        try
        {
            if (CanRun is null || CanRun(context))
            {
                return null;
            }

            return WhyNot?.Invoke(context) ?? "Not available in this context.";
        }
        catch
        {
            return "Not available in this context.";
        }
    }
}

/// <summary>
/// Every action of the application, added once at startup. Ids are unique on purpose: a duplicate is
/// a mistake in the code, never a choice between two implementations. The palette, a future keymap
/// and agents all read this one list, so adding an action in one place makes it available everywhere.
/// </summary>
public sealed class ActionRegistry
{
    private readonly Dictionary<string, IdeAction> _byId = new(StringComparer.Ordinal);
    private readonly List<IdeAction> _ordered = [];
    private IReadOnlyList<IdeAction>? _sorted;

    /// <summary>Raised after an action ran successfully.</summary>
    public event EventHandler<IdeAction>? Executed;

    /// <summary>Adds an action; an id added twice is a programming error.</summary>
    public void Add(IdeAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (string.IsNullOrWhiteSpace(action.Id))
        {
            throw new ArgumentException("An action needs an id.", nameof(action));
        }

        if (!_byId.TryAdd(action.Id, action))
        {
            throw new InvalidOperationException($"The action '{action.Id}' is declared twice.");
        }

        _ordered.Add(action);
        _sorted = null;
    }

    /// <summary>Every action, by group and then title.</summary>
    public IReadOnlyList<IdeAction> All => _sorted ??= _ordered
        .OrderBy(action => action.Group, StringComparer.OrdinalIgnoreCase)
        .ThenBy(action => action.Title, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>The action with this id, or null.</summary>
    public IdeAction? Find(string id)
        => _byId.TryGetValue(id, out IdeAction? action) ? action : null;

    /// <summary>
    /// The actions matching every word of <paramref name="query"/> in the title, group or id,
    /// ignoring case. An empty query returns everything.
    /// </summary>
    public IReadOnlyList<IdeAction> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return All;
        }

        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return All.Where(action => words.All(word => Matches(action, word))).ToArray();
    }

    /// <summary>
    /// Runs an action, if it exists and can run in <paramref name="context"/>. Returns false when
    /// there is no such action or the action refuses the context.
    /// </summary>
    public async Task<bool> RunAsync(
        string id,
        ActionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        IdeAction? action = Find(id);
        if (action is null)
        {
            return false;
        }

        ActionContext effective = context ?? new ActionContext();
        if (!action.CanExecute(effective))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await action.Run(effective).ConfigureAwait(true);
        Executed?.Invoke(this, action);
        return true;
    }

    private static bool Matches(IdeAction action, string word)
        => action.Title.Contains(word, StringComparison.OrdinalIgnoreCase)
           || action.Group.Contains(word, StringComparison.OrdinalIgnoreCase)
           || action.Id.Contains(word, StringComparison.OrdinalIgnoreCase);
}
