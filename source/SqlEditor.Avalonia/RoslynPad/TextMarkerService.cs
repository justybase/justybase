// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace JustyBase.Editor;

public sealed class TextMarkerService : DocumentColorizingTransformer, IBackgroundRenderer, ITextViewConnect
{
    private readonly TextSegmentCollection<TextMarker> _markers;
    private readonly TextDocument _document;
    private readonly List<TextView> _textViews = new();
    private readonly object _textViewsLock = new();

    public TextMarkerService(CodeTextEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        _document = editor.Document;
        _markers = new TextSegmentCollection<TextMarker>(_document);
        editor.ToolTipRequest += EditorOnToolTipRequest;
    }

    private void EditorOnToolTipRequest(object? sender, ToolTipRequestEventArgs args)
    {
        var offset = _document.GetOffset(args.LogicalPosition);
        var markersAtOffset = GetMarkersAtOffset(offset);
        var markerWithToolTip = markersAtOffset.FirstOrDefault(marker => marker.ToolTip != null);
        if (markerWithToolTip != null && markerWithToolTip.ToolTip != null)
        {
            args.SetToolTip(markerWithToolTip.ToolTip);
        }
    }

    public TextMarker? TryCreate(int startOffset, int length)
    {
        var textLength = _document.TextLength;
        if (startOffset < 0 || startOffset > textLength) return null;
        if (length < 0 || startOffset + length > textLength) return null;

        var marker = new TextMarker(this, startOffset, length);
        lock (_markers)
        {
            _markers.Add(marker);
        }
        return marker;
    }

    public IEnumerable<TextMarker> GetMarkersAtOffset(int offset)
    {
        lock (_markers)
        {
            return _markers.FindSegmentsContaining(offset).ToArray();
        }
    }

    public IEnumerable<TextMarker> TextMarkers
    {
        get
        {
            lock (_markers)
            {
                return _markers.ToArray();
            }
        }
    }

    public void RemoveAll(Predicate<TextMarker> predicate)
    {
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));
        TextMarker[] toRemove;
        lock (_markers)
        {
            toRemove = _markers.Where(m => predicate(m)).ToArray();
            foreach (var m in toRemove)
                _markers.Remove(m);
        }
        foreach (var m in toRemove)
        {
            Redraw(m);
            m.OnDeleted();
        }
    }

    public void Remove(TextMarker marker)
    {
        if (marker == null)
            throw new ArgumentNullException(nameof(marker));
        bool removed;
        lock (_markers)
        {
            removed = _markers.Remove(marker);
        }
        if (removed)
        {
            Redraw(marker);
            marker.OnDeleted();
        }
    }

    internal void Redraw(ISegment segment)
    {
        List<TextView> views;
        lock (_textViewsLock)
        {
            views = new List<TextView>(_textViews);
        }
        foreach (var view in views)
        {
            view.Redraw(segment);
        }
        RedrawRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? RedrawRequested;

    private static readonly ConcurrentDictionary<Color, CommonBrush> _brushCache = [];
    private static readonly ConcurrentDictionary<Color, TextDecorationCollection> _decorationCache = [];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CommonBrush GetCachedBrush(Color color)
    {
        if (_brushCache.TryGetValue(color, out var existing))
            return existing;
        var brush = new SolidColorBrush(color).AsFrozen();
        _brushCache.TryAdd(color, brush);
        return brush;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TextDecorationCollection GetCachedDecorations(Color underlineColor)
    {
        if (_decorationCache.TryGetValue(underlineColor, out var existing))
            return existing;
        var brush = new SolidColorBrush(underlineColor).AsFrozen();
        var decoration = new TextDecoration
        {
            Stroke = brush,
            StrokeThickness = 1,
            Location = TextDecorationLocation.Underline
        };
        var decorations = new TextDecorationCollection([decoration]);
        _decorationCache.TryAdd(underlineColor, decorations);
        return decorations;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        // Snapshot markers under lock to avoid concurrent modification
        IReadOnlyList<TextMarker> lineMarkers;
        lock (_markers)
        {
            if (_markers.Count == 0) return;
            lineMarkers = _markers.FindOverlappingSegments(line.Offset, line.Length).ToArray();
        }

        var lineStart = line.Offset;
        var lineEnd = lineStart + line.Length;
        foreach (var marker in lineMarkers)
        {
            CommonBrush? foregroundBrush = null;
            if (marker.ForegroundColor != null)
                foregroundBrush = GetCachedBrush(marker.ForegroundColor.Value);

            CommonBrush? backgroundBrush = null;
            TextDecorationCollection? decorations = null;
            if (marker.MarkerColor.A > 0)
            {
                var mc = marker.MarkerColor;
                var bgColor = Color.FromArgb(30, mc.R, mc.G, mc.B);
                backgroundBrush = GetCachedBrush(bgColor);
                decorations = GetCachedDecorations(marker.MarkerColor);
            }

            var needTypeface = marker.FontStyle is not null || marker.FontWeight is not null;
            var localForeground = foregroundBrush;
            var localBackground = backgroundBrush;
            var localDecorations = decorations;
            var localFontStyle = marker.FontStyle;
            var localFontWeight = marker.FontWeight;
            ChangeLinePart(
                Math.Max(marker.StartOffset, lineStart),
                Math.Min(marker.EndOffset, lineEnd),
                element =>
                {
                    if (localForeground != null)
                        element.TextRunProperties.SetForegroundBrush(localForeground);
                    if (localBackground != null)
                        element.TextRunProperties.SetBackgroundBrush(localBackground);
                    if (localDecorations != null)
                        element.TextRunProperties.SetTextDecorations(localDecorations);
                    if (needTypeface)
                    {
                        var tf = element.TextRunProperties.Typeface;
                        element.TextRunProperties.SetTypeface(new Typeface(
                            tf.FontFamily,
                            localFontStyle ?? tf.Style,
                            localFontWeight ?? tf.Weight,
                            tf.Stretch
                        ));
                    }
                }
            );
        }
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView == null)
            throw new ArgumentNullException(nameof(textView));
        if (drawingContext == null)
            throw new ArgumentNullException(nameof(drawingContext));
        if (!textView.VisualLinesValid) return;
        var visualLines = textView.VisualLines;
        if (visualLines.Count == 0) return;
        var viewStart = visualLines.First().FirstDocumentLine.Offset;
        var viewEnd = visualLines.Last().LastDocumentLine.EndOffset;

        IReadOnlyList<TextMarker> viewMarkers;
        lock (_markers)
        {
            if (_markers.Count == 0) return;
            viewMarkers = _markers.FindOverlappingSegments(viewStart, viewEnd - viewStart).ToArray();
        }

        foreach (var marker in viewMarkers)
        {
            if (marker.BackgroundColor != null)
            {
                var geoBuilder = new BackgroundGeometryBuilder
                {
                    AlignToWholePixels = true,
                    CornerRadius = 3
                };
                geoBuilder.AddSegment(textView, marker);
                var geometry = geoBuilder.CreateGeometry();
                if (geometry != null)
                {
                    var brush = GetCachedBrush(marker.BackgroundColor.Value);
                    drawingContext.DrawGeometry(brush, null, geometry);
                }
            }
        }
    }

    void ITextViewConnect.AddToTextView(TextView textView)
    {
        if (textView == null) return;
        lock (_textViewsLock)
        {
            if (!_textViews.Contains(textView))
            {
                Debug.Assert(textView.Document == _document);
                _textViews.Add(textView);
            }
        }
    }

    void ITextViewConnect.RemoveFromTextView(TextView textView)
    {
        if (textView == null) return;
        lock (_textViewsLock)
        {
            _textViews.Remove(textView);
        }
    }
}
