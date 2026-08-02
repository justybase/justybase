namespace JustyBase.Services;

/// <summary>Transactional preferences draft (Legacy PreferencesViewModel pattern).</summary>
public sealed class TransactionalPreferencesDraft<T>
{
    private T? _draft;
    private T? _committed;

    public TransactionalPreferencesDraft(T initial)
    {
        _committed = initial;
        _draft = initial;
    }

    public T Draft => _draft!;
    public T Committed => _committed!;

    public void BeginEdit(T snapshot) => _draft = snapshot;

    public void Save()
    {
        _committed = _draft;
    }

    public void Cancel() => _draft = _committed;
}
