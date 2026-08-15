namespace JustyBase.Services.DataGrid;

public enum ResultGridKeyboardAction
{
    None,
    Copy,
    CopyAll,
    Find,
    FindNext,
    FindPrevious
}

public interface IResultGridKeyboardService
{
    ResultGridKeyboardAction ParseKeyDown(Key key, KeyModifiers modifiers);
}
