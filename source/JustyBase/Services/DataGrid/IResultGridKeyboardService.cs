namespace JustyBase.Services.DataGrid;

public enum ResultGridKeyboardAction
{
    None,
    Copy,
    CopyAll
}

public interface IResultGridKeyboardService
{
    ResultGridKeyboardAction ParseKeyDown(Key key, KeyModifiers modifiers);
}
