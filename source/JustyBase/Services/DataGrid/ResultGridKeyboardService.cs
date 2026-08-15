namespace JustyBase.Services.DataGrid;

public sealed class ResultGridKeyboardService : IResultGridKeyboardService
{
    public ResultGridKeyboardAction ParseKeyDown(Key key, KeyModifiers modifiers)
    {
        if (modifiers == KeyModifiers.Control)
        {
            return key switch
            {
                Key.C => ResultGridKeyboardAction.Copy,
                Key.A => ResultGridKeyboardAction.CopyAll,
                Key.F => ResultGridKeyboardAction.Find,
                _ => ResultGridKeyboardAction.None
            };
        }

        if (key == Key.F3)
        {
            return modifiers == KeyModifiers.Shift
                ? ResultGridKeyboardAction.FindPrevious
                : ResultGridKeyboardAction.FindNext;
        }

        return ResultGridKeyboardAction.None;
    }
}
