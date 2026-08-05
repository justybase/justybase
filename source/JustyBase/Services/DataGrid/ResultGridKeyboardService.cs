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
                _ => ResultGridKeyboardAction.None
            };
        }

        return ResultGridKeyboardAction.None;
    }
}
