namespace JustyBase.Editor;

//roslyn code editor
public partial class CodeTextEditor
{
    protected override Type StyleKeyOverride => typeof(TextEditor);

    partial void Initialize()
    {
        AddHandler(PointerHoverEvent, OnPointerHover);
        AddHandler(PointerHoverStoppedEvent, OnPointerHoverStopped);
    }

    private ToolTip? _toolTip;

    private async void OnPointerHover(object? sender, PointerEventArgs e)
    {
        TextViewPosition? position;
        try
        {
            position = TextArea.TextView.GetPositionFloor(e.GetPosition(TextArea.TextView) + TextArea.TextView.ScrollOffset);
        }
        catch (ArgumentOutOfRangeException)
        {
            e.Handled = true;
            return;
        }
        var args = new ToolTipRequestEventArgs { InDocument = position.HasValue };
        if (!position.HasValue || position.Value.Location.IsEmpty || position.Value.IsAtEndOfLine)
            return;

        args.LogicalPosition = position.Value.Location;
        args.Position = Document.GetOffset(position.Value.Line, position.Value.Column);

        RaiseEvent(args);

        if (args.ContentToShow == null)
        {
            var asyncRequest = AsyncToolTipRequest?.Invoke(args);
            if (asyncRequest != null)
            {
                await asyncRequest;
            }
        }

        if (args.ContentToShow == null)
            return;

        if (_toolTip == null)
        {
            _toolTip = new ToolTip { MaxWidth = 400 };
            InitializeToolTip();
        }

        if (args.ContentToShow is string stringContent)
        {
            ToolTip.SetTip(this, new TextBlock
            {
                Text = stringContent,
                TextWrapping = TextWrapping.Wrap
            });
        }
        else
        {
            ToolTip.SetTip(this, new ContentPresenter
            {
                Content = args.ContentToShow,
                MaxWidth = 400
            });
        }

        e.Handled = true;
        ToolTip.SetIsOpen(this, true);
        AfterToolTipOpen();
    }

    private void OnPointerHoverStopped(object? sender, PointerEventArgs e)
    {
        if (_toolTip != null)
        {
            ToolTip.SetIsOpen(this, false);
            _toolTip = null;
            e.Handled = true;
        }
    }
}
