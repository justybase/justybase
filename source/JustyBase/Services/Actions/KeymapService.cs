using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Input;
using JustyBase.Common.Contracts;
using JustyBase.Services.Files;

namespace JustyBase.Services.Actions;

/// <summary>One action and the gesture it currently uses.</summary>
public sealed record KeymapEntry(IdeAction Action, string? Gesture, bool IsCustom);

/// <summary>A gesture that more than one action would answer to.</summary>
public sealed record KeymapConflict(string Gesture, IReadOnlyList<IdeAction> Actions);

/// <summary>The result of trying to bind a gesture.</summary>
public sealed record KeymapUpdateResult(
    bool Success,
    string? Gesture,
    IReadOnlyList<KeymapConflict> Conflicts,
    string? Error)
{
    public static KeymapUpdateResult Ok(string? gesture) => new(true, gesture, [], null);

    public static KeymapUpdateResult Failed(string error, IReadOnlyList<KeymapConflict>? conflicts = null)
        => new(false, null, conflicts ?? [], error);
}

/// <summary>
/// Stores the person's keyboard overrides on top of the defaults declared by <see cref="IdeAction"/>.
/// A binding is only saved when it does not collide with another action; that check is what makes the
/// editor safe to use without a separate "conflict mode".
/// </summary>
public sealed class KeymapService
{
    private readonly ActionRegistry _registry;
    private readonly string _filePath;
    private readonly Dictionary<string, string> _overrides = new(StringComparer.Ordinal);
    private bool _loaded;

    /// <summary>Gestures handled by the shell itself (window bindings and the side-panel toggle);
    /// they are outside the registry, so an action binding to them would never run.</summary>
    private static readonly HashSet<string> ReservedGestures = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ctrl+T",
        "Ctrl+Tab",
        "Ctrl+Shift+Tab",
        "Ctrl+B",
    };

    public KeymapService(ActionRegistry registry)
        : this(registry, Path.Combine(IGeneralApplicationData.ConfigDirectoryEvo, "keymap.json"))
    {
    }

    public KeymapService(ActionRegistry registry, string filePath)
    {
        _registry = registry;
        _filePath = Path.GetFullPath(filePath);
    }

    /// <summary>Raised after a binding changed (or was reset).</summary>
    public event EventHandler? Changed;

    /// <summary>The gesture currently bound to <paramref name="actionId"/>, or null when unbound.</summary>
    public string? GetGesture(string actionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        EnsureLoaded();

        if (_overrides.TryGetValue(actionId, out string? custom))
        {
            return string.IsNullOrWhiteSpace(custom) ? null : custom;
        }

        return _registry.Find(actionId)?.Shortcut;
    }

    /// <summary>Every action with its current gesture, by group and title.</summary>
    public IReadOnlyList<KeymapEntry> GetAll()
    {
        EnsureLoaded();
        return _registry.All
            .Select(action => new KeymapEntry(
                action,
                GetGesture(action.Id),
                _overrides.ContainsKey(action.Id)))
            .ToArray();
    }

    /// <summary>Gestures used by more than one action; an empty list means the map is consistent.</summary>
    public IReadOnlyList<KeymapConflict> FindConflicts()
    {
        EnsureLoaded();
        return _registry.All
            .Select(action => (Action: action, Gesture: GetGesture(action.Id)))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Gesture))
            .GroupBy(pair => NormalizeForKey(pair.Gesture!), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new KeymapConflict(
                group.First().Gesture!,
                group.Select(pair => pair.Action).ToArray()))
            .ToArray();
    }

    /// <summary>
    /// Binds <paramref name="gesture"/> to the action. An empty gesture removes the binding. The change
    /// is refused when another action already uses the same gesture.
    /// </summary>
    public async Task<KeymapUpdateResult> SetGestureAsync(
        string actionId,
        string? gesture,
        CancellationToken cancellationToken = default)
    {
        EnsureLoaded();
        IdeAction? action = _registry.Find(actionId);
        if (action is null)
        {
            return KeymapUpdateResult.Failed($"Unknown action '{actionId}'.");
        }

        string? normalized;
        if (string.IsNullOrWhiteSpace(gesture))
        {
            normalized = null;
        }
        else if (!TryNormalize(gesture, out normalized, out string? error))
        {
            return KeymapUpdateResult.Failed(error!);
        }

        if (normalized is not null && ReservedGestures.Contains(normalized))
        {
            return KeymapUpdateResult.Failed($"{normalized} is reserved by the application.");
        }

        if (normalized is not null)
        {
            IdeAction[] clashes = _registry.All
                .Where(other => !string.Equals(other.Id, actionId, StringComparison.Ordinal))
                .Where(other => GestureEquals(GetGesture(other.Id), normalized))
                .ToArray();

            if (clashes.Length > 0)
            {
                string names = string.Join(", ", clashes.Select(item => item.Title));
                return KeymapUpdateResult.Failed(
                    $"{normalized} is already used by {names}.",
                    [new KeymapConflict(normalized, clashes)]);
            }
        }

        bool hadPrevious = _overrides.TryGetValue(actionId, out string? previous);
        if (string.Equals(normalized, action.Shortcut, StringComparison.OrdinalIgnoreCase))
        {
            _overrides.Remove(actionId);
        }
        else
        {
            _overrides[actionId] = normalized ?? string.Empty;
        }

        try
        {
            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Persisting failed; keep the in-memory map in sync with the file that was not written.
            if (hadPrevious)
            {
                _overrides[actionId] = previous!;
            }
            else
            {
                _overrides.Remove(actionId);
            }

            throw;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return KeymapUpdateResult.Ok(normalized);
    }

    /// <summary>Returns one action to its declared default.</summary>
    public async Task ResetAsync(string actionId, CancellationToken cancellationToken = default)
    {
        EnsureLoaded();
        if (!_overrides.TryGetValue(actionId, out string previous))
        {
            return;
        }

        _overrides.Remove(actionId);
        try
        {
            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _overrides[actionId] = previous;
            throw;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Returns every action to its declared default.</summary>
    public async Task ResetAllAsync(CancellationToken cancellationToken = default)
    {
        EnsureLoaded();
        if (_overrides.Count == 0)
        {
            return;
        }

        var snapshot = new Dictionary<string, string>(_overrides, StringComparer.Ordinal);
        _overrides.Clear();
        try
        {
            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            foreach (var pair in snapshot)
            {
                _overrides[pair.Key] = pair.Value;
            }

            throw;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(_filePath, Encoding.UTF8);
            KeymapFile? file = JsonSerializer.Deserialize(json, KeymapJsonContext.Default.KeymapFile);
            if (file?.Bindings is null)
            {
                return;
            }

            foreach (var pair in file.Bindings)
            {
                _overrides[pair.Key] = pair.Value ?? string.Empty;
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException)
        {
            // A broken keymap must never stop the application from starting.
            TryRenameBrokenFile();
            _overrides.Clear();
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var file = new KeymapFile
        {
            Bindings = new Dictionary<string, string>(_overrides, StringComparer.Ordinal),
        };

        string json = JsonSerializer.Serialize(file, KeymapJsonContext.Default.KeymapFile);
        await SafeFileWriter.WriteAsync(_filePath, json, TextFileFormat.Default, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool TryNormalize(string gesture, out string? normalized, out string? error)
    {
        normalized = null;
        error = null;

        KeyGesture parsed;
        try
        {
            parsed = KeyGesture.Parse(gesture);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            error = $"'{gesture}' is not a valid shortcut.";
            return false;
        }

        if (parsed.Key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin)
        {
            error = "A shortcut needs a non-modifier key, for example Ctrl+Shift+O.";
            return false;
        }

        normalized = parsed.ToString();
        return true;
    }

    private static string NormalizeForKey(string? gesture)
        => string.IsNullOrWhiteSpace(gesture) ? string.Empty : gesture.Trim();

    private static bool GestureEquals(string? left, string? right)
        => string.Equals(NormalizeForKey(left), NormalizeForKey(right), StringComparison.OrdinalIgnoreCase);

    private void TryRenameBrokenFile()
    {
        try
        {
            File.Move(_filePath, _filePath + ".bad", overwrite: true);
        }
        catch (IOException)
        {
            // best effort
        }
        catch (UnauthorizedAccessException)
        {
            // best effort
        }
    }
}

internal sealed class KeymapFile
{
    public int Version { get; set; } = 1;

    public Dictionary<string, string> Bindings { get; set; } = new(StringComparer.Ordinal);
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(KeymapFile))]
internal sealed partial class KeymapJsonContext : JsonSerializerContext
{
}
