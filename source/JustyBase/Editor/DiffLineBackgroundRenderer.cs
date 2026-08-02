using DiffPlex.DiffBuilder.Model;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace JustyBase.Editor;

/// <summary>Paints DiffPlex side-by-side line backgrounds in an AvaloniaEdit TextView.</summary>
public sealed class DiffLineBackgroundRenderer : IBackgroundRenderer
{
    private readonly IReadOnlyList<ChangeType> _lineKinds;
    private static readonly IBrush DeletedBrush = new SolidColorBrush(Color.Parse("#1AFF0000"));
    private static readonly IBrush InsertedBrush = new SolidColorBrush(Color.Parse("#199BB955"));
    private static readonly IBrush ModifiedBrush = new SolidColorBrush(Color.Parse("#19E2C080"));
    private static readonly IBrush ImaginaryBrush = new SolidColorBrush(Color.Parse("#18000000"));

    public DiffLineBackgroundRenderer(IReadOnlyList<ChangeType> lineKinds)
    {
        _lineKinds = lineKinds ?? [];
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || textView.Document is null || textView.VisualLines.Count == 0)
            return;

        foreach (VisualLine visualLine in textView.VisualLines)
        {
            int lineNumber = visualLine.FirstDocumentLine.LineNumber;
            if (lineNumber < 1 || lineNumber > _lineKinds.Count)
                continue;

            IBrush? brush = BrushFor(_lineKinds[lineNumber - 1]);
            if (brush is null)
                continue;

            DocumentLine docLine = visualLine.FirstDocumentLine;
            foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, docLine))
            {
                // Extend to full editor width so empty/imaginary lines still show tint.
                var full = new Rect(0, rect.Y, textView.Bounds.Width, Math.Max(rect.Height, visualLine.Height));
                drawingContext.FillRectangle(brush, full);
            }
        }
    }

    private static IBrush? BrushFor(ChangeType kind) => kind switch
    {
        ChangeType.Deleted => DeletedBrush,
        ChangeType.Inserted => InsertedBrush,
        ChangeType.Modified => ModifiedBrush,
        ChangeType.Imaginary => ImaginaryBrush,
        _ => null
    };
}
