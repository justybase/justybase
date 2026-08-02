using System.Net;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using LiveMarkdown.Avalonia;

namespace JustyBase.Views.Tools;

/// <summary>
/// Hosts LiveMarkdown for one chat message and bridges model updates to the
/// renderer's append-only streaming API.
/// </summary>
public sealed class ChatMarkdownView : UserControl
{
    public static readonly StyledProperty<string> MarkdownTextProperty =
        AvaloniaProperty.Register<ChatMarkdownView, string>(nameof(MarkdownText), string.Empty);

    public static readonly StyledProperty<bool> IsStreamingProperty =
        AvaloniaProperty.Register<ChatMarkdownView, bool>(nameof(IsStreaming));

    private const int UpdateIntervalMilliseconds = 60;

    private readonly MarkdownRenderer _renderer;
    private readonly ChatMarkdownStreamBuffer _streamBuffer = new();
    private DispatcherTimer? _updateTimer;
    private SelectableTextBlock? _fallbackTextBlock;
    private string _pendingText = string.Empty;
    private long _lastAcceptedRequest;
    private bool _isAttached;
    private bool _usingFallback;

    public ChatMarkdownView()
    {
        _renderer = new MarkdownRenderer();
        _renderer.LinkClick += OnLinkClick;
        Content = _renderer;
    }

    public string MarkdownText
    {
        get => GetValue(MarkdownTextProperty);
        set => SetValue(MarkdownTextProperty, value ?? string.Empty);
    }

    public bool IsStreaming
    {
        get => GetValue(IsStreamingProperty);
        set => SetValue(IsStreamingProperty, value);
    }

    internal string RenderedMarkdown => _streamBuffer.RenderedText;

    internal bool IsUsingFallback => _usingFallback;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MarkdownTextProperty)
        {
            RequestTextUpdate(change.GetNewValue<string>() ?? string.Empty);
        }
        else if (change.Property == IsStreamingProperty && !change.GetNewValue<bool>())
        {
            RequestFlush();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;

        if (!_usingFallback)
        {
            EnsureUpdateTimer();
            _pendingText = MarkdownText;
            ApplyPendingText();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;

        if (_updateTimer is not null)
        {
            _updateTimer.Stop();
            _updateTimer.Tick -= OnUpdateTimerTick;
            _updateTimer = null;
        }

        _renderer.MarkdownBuilder = null!;
        _streamBuffer.Reset();

        base.OnDetachedFromVisualTree(e);
    }

    private void RequestTextUpdate(string text)
    {
        var request = Interlocked.Increment(ref _lastAcceptedRequest);
        if (Dispatcher.UIThread.CheckAccess())
        {
            AcceptTextUpdate(request, text);
            return;
        }

        Dispatcher.UIThread.Post(() => AcceptTextUpdate(request, text));
    }

    private void RequestFlush()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            FlushPendingText();
            return;
        }

        Dispatcher.UIThread.Post(FlushPendingText);
    }

    private void FlushPendingText()
    {
        _pendingText = MarkdownText;
        ApplyPendingText();
    }

    private void AcceptTextUpdate(long request, string text)
    {
        if (request < _lastAcceptedRequest)
        {
            return;
        }

        _pendingText = text;
        if (!_isAttached)
        {
            return;
        }

        if (_usingFallback)
        {
            if (_fallbackTextBlock is not null)
            {
                _fallbackTextBlock.Text = ChatMarkdownSanitizer.Sanitize(text);
            }

            return;
        }

        EnsureUpdateTimer();
        _updateTimer!.Start();
    }

    private void EnsureUpdateTimer()
    {
        if (_updateTimer is not null)
        {
            return;
        }

        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(UpdateIntervalMilliseconds)
        };
        _updateTimer.Tick += OnUpdateTimerTick;
    }

    private void OnUpdateTimerTick(object? sender, EventArgs e)
    {
        ApplyPendingText();
    }

    private void ApplyPendingText()
    {
        if (!_isAttached || _usingFallback)
        {
            return;
        }

        _updateTimer?.Stop();

        try
        {
            _renderer.MarkdownBuilder ??= new ObservableStringBuilder();
            _streamBuffer.Apply(
                _pendingText,
                () => _renderer.MarkdownBuilder.Clear(),
                value => _renderer.MarkdownBuilder.Append(value));
        }
        catch
        {
            ShowFallback();
        }
    }

    private void ShowFallback()
    {
        _usingFallback = true;
        _renderer.MarkdownBuilder = null!;

        _fallbackTextBlock ??= new SelectableTextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            LineHeight = 17
        };
        _fallbackTextBlock.Text = ChatMarkdownSanitizer.Sanitize(MarkdownText);
        Content = _fallbackTextBlock;
    }

    private void OnLinkClick(object? sender, LinkClickedEventArgs e)
    {
        // The adapter removes every non-http(s) Markdown destination before it
        // reaches the renderer. Keep this event as a second line of defence in
        // case a future pipeline extension creates links outside that adapter.
        if (!ChatMarkdownSanitizer.IsAllowedHttpLink(e.HRef?.ToString()))
        {
            e.Handled = true;
        }
    }
}

/// <summary>
/// Keeps the renderer append-only when the source grows, while allowing a
/// complete reset when Markdown sanitization changes an earlier suffix.
/// </summary>
internal sealed class ChatMarkdownStreamBuffer
{
    public string RenderedText { get; private set; } = string.Empty;

    public bool Apply(string source, Action clear, Action<string> append)
    {
        var safeText = ChatMarkdownSanitizer.Sanitize(source);
        if (string.Equals(safeText, RenderedText, StringComparison.Ordinal))
        {
            return false;
        }

        if (RenderedText.Length > 0 && safeText.StartsWith(RenderedText, StringComparison.Ordinal))
        {
            var suffix = safeText[RenderedText.Length..];
            if (suffix.Length > 0)
            {
                append(suffix);
            }
        }
        else
        {
            clear();
            if (safeText.Length > 0)
            {
                append(safeText);
            }
        }

        RenderedText = safeText;
        return true;
    }

    public void Reset() => RenderedText = string.Empty;
}

internal static class ChatMarkdownSanitizer
{
    public static string Sanitize(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return string.Empty;
        }

        var result = new StringBuilder(markdown.Length);
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var unsafeReferences = FindUnsafeReferences(lines);
        var inFence = false;
        char fenceCharacter = '\0';

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (TryGetFence(line, out var currentFenceCharacter))
            {
                if (!inFence)
                {
                    inFence = true;
                    fenceCharacter = currentFenceCharacter;
                }
                else if (fenceCharacter == currentFenceCharacter)
                {
                    inFence = false;
                    fenceCharacter = '\0';
                }

                result.Append(line);
            }
            else if (inFence)
            {
                result.Append(line);
            }
            else if (TryReadReferenceDefinition(line, out var referenceId, out var referenceDestination)
                && unsafeReferences.Contains(NormalizeReference(referenceId))
                && !IsAllowedHttpLink(referenceDestination))
            {
                // Do not leave an unsafe definition for Markdig to interpret as
                // an active reference. Keep a harmless, readable placeholder.
                var start = line.IndexOf('[');
                result.Append(line[..start])
                    .Append('[')
                    .Append(referenceId)
                    .Append("] [blocked link reference]");
            }
            else
            {
                AppendSanitizedInline(line, result, unsafeReferences);
            }

            if (i < lines.Length - 1)
            {
                result.Append('\n');
            }
        }

        return result.ToString();
    }

    public static bool IsAllowedHttpLink(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return false;
        }

        var decodedHref = WebUtility.HtmlDecode(href).Trim();
        return Uri.TryCreate(decodedHref, UriKind.Absolute, out var uri)
            && uri.Host.Length > 0
            && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }

    private static void AppendSanitizedInline(
        string line,
        StringBuilder result,
        ISet<string>? unsafeReferences = null)
    {
        var inCodeSpan = false;
        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];

            if (current == '`')
            {
                inCodeSpan = !inCodeSpan;
                result.Append(current);
                continue;
            }

            if (inCodeSpan)
            {
                result.Append(current);
                continue;
            }

            if (current == '!' && index + 1 < line.Length && line[index + 1] == '['
                && TryReadImage(line, index + 1, out var imageLabel, out var imageEnd))
            {
                AppendSanitizedInline(imageLabel, result, unsafeReferences);
                index = imageEnd;
                continue;
            }

            if (current == '['
                && TryReadLabelAndDestination(line, index, out var linkLabel, out var linkEnd, out var destination))
            {
                if (IsAllowedHttpLink(destination))
                {
                    result.Append('[');
                    AppendSanitizedInline(linkLabel, result, unsafeReferences);
                    result.Append("](").Append(destination).Append(')');
                }
                else
                {
                    AppendSanitizedInline(linkLabel, result, unsafeReferences);
                }

                index = linkEnd;
                continue;
            }

            if (current == '['
                && TryReadReference(line, index, out var referenceLabel, out var referenceEnd, out var referenceId))
            {
                if (unsafeReferences is not null
                    && unsafeReferences.Contains(NormalizeReference(referenceId)))
                {
                    AppendSanitizedInline(referenceLabel, result, unsafeReferences);
                }
                else
                {
                    result.Append('[');
                    AppendSanitizedInline(referenceLabel, result, unsafeReferences);
                    result.Append("][").Append(referenceId).Append(']');
                }

                index = referenceEnd;
                continue;
            }

            if (current == '<')
            {
                var closingBracket = line.IndexOf('>', index + 1);
                if (closingBracket > index)
                {
                    var candidate = line[(index + 1)..closingBracket].Trim();
                    if (IsAllowedHttpLink(candidate))
                    {
                        result.Append('[').Append(candidate).Append("](").Append(candidate).Append(')');
                    }
                    else
                    {
                        result.Append("&lt;");
                        result.Append(line, index + 1, closingBracket - index - 1);
                        result.Append("&gt;");
                    }

                    index = closingBracket;
                    continue;
                }

                result.Append("&lt;");
                continue;
            }

            if (current == '>' && IsBlockQuoteMarker(line, index))
            {
                result.Append(current);
                continue;
            }

            if (current == '>')
            {
                result.Append("&gt;");
                continue;
            }

            result.Append(current);
        }
    }

    private static bool IsBlockQuoteMarker(string line, int index)
    {
        if (index > 3)
        {
            return false;
        }

        for (var i = 0; i < index; i++)
        {
            if (line[i] != ' ')
            {
                return false;
            }
        }

        return index + 1 == line.Length || char.IsWhiteSpace(line[index + 1]);
    }

    private static bool TryReadLabelAndDestination(
        string line,
        int labelStart,
        out string label,
        out int end,
        out string destination)
    {
        label = string.Empty;
        destination = string.Empty;
        end = labelStart;

        var labelEnd = FindClosingBracket(line, labelStart);
        if (labelEnd < 0 || labelEnd + 1 >= line.Length || line[labelEnd + 1] != '(')
        {
            return false;
        }

        var destinationEnd = FindClosingParenthesis(line, labelEnd + 1);
        if (destinationEnd < 0)
        {
            return false;
        }

        label = line[(labelStart + 1)..labelEnd];
        destination = ExtractDestination(line[(labelEnd + 2)..destinationEnd]);
        end = destinationEnd;
        return true;
    }

    private static bool TryReadImage(string line, int labelStart, out string label, out int end)
    {
        label = string.Empty;
        end = labelStart;

        var labelEnd = FindClosingBracket(line, labelStart);
        if (labelEnd < 0)
        {
            return false;
        }

        // Inline, full and shortcut reference images are all rendered as
        // selectable alt text. No image destination is passed to LiveMarkdown.
        if (labelEnd + 1 < line.Length && line[labelEnd + 1] == '(')
        {
            var destinationEnd = FindClosingParenthesis(line, labelEnd + 1);
            if (destinationEnd < 0)
            {
                return false;
            }

            end = destinationEnd;
        }
        else if (labelEnd + 1 < line.Length && line[labelEnd + 1] == '[')
        {
            var referenceEnd = FindClosingBracket(line, labelEnd + 1);
            if (referenceEnd < 0)
            {
                return false;
            }

            end = referenceEnd;
        }
        else
        {
            end = labelEnd;
        }

        label = line[(labelStart + 1)..labelEnd];
        return true;
    }

    private static bool TryReadReference(
        string line,
        int labelStart,
        out string label,
        out int end,
        out string referenceId)
    {
        label = string.Empty;
        referenceId = string.Empty;
        end = labelStart;

        var labelEnd = FindClosingBracket(line, labelStart);
        if (labelEnd < 0 || labelEnd + 1 >= line.Length || line[labelEnd + 1] != '[')
        {
            return false;
        }

        var referenceEnd = FindClosingBracket(line, labelEnd + 1);
        if (referenceEnd < 0)
        {
            return false;
        }

        label = line[(labelStart + 1)..labelEnd];
        referenceId = line[(labelEnd + 2)..referenceEnd];
        if (referenceId.Length == 0)
        {
            referenceId = label;
        }

        end = referenceEnd;
        return true;
    }

    private static HashSet<string> FindUnsafeReferences(IReadOnlyList<string> lines)
    {
        var unsafeReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            if (TryReadReferenceDefinition(line, out var referenceId, out var destination)
                && !IsAllowedHttpLink(destination))
            {
                unsafeReferences.Add(NormalizeReference(referenceId));
            }
        }

        return unsafeReferences;
    }

    private static bool TryReadReferenceDefinition(string line, out string referenceId, out string destination)
    {
        referenceId = string.Empty;
        destination = string.Empty;
        var start = 0;
        while (start < line.Length && start < 3 && line[start] == ' ')
        {
            start++;
        }

        if (start >= line.Length || line[start] != '[')
        {
            return false;
        }

        var labelEnd = FindClosingBracket(line, start);
        if (labelEnd < 0 || labelEnd + 1 >= line.Length || line[labelEnd + 1] != ':')
        {
            return false;
        }

        referenceId = line[(start + 1)..labelEnd];
        destination = ExtractDestination(line[(labelEnd + 2)..]);
        return referenceId.Length > 0 && destination.Length > 0;
    }

    private static string NormalizeReference(string referenceId) => referenceId.Trim();

    private static int FindClosingParenthesis(string line, int openingParenthesis)
    {
        var nesting = 0;
        for (var i = openingParenthesis; i < line.Length; i++)
        {
            if (line[i] == '(')
            {
                nesting++;
            }
            else if (line[i] == ')' && --nesting == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindClosingBracket(string line, int openingBracket)
    {
        var nesting = 0;
        for (var i = openingBracket; i < line.Length; i++)
        {
            if (line[i] == '[')
            {
                nesting++;
            }
            else if (line[i] == ']' && --nesting == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static string ExtractDestination(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith('<') && trimmed.IndexOf('>') is var closing && closing > 0)
        {
            return trimmed[1..closing];
        }

        var whitespace = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        return whitespace >= 0 ? trimmed[..whitespace] : trimmed;
    }

    private static bool TryGetFence(string line, out char fenceCharacter)
    {
        fenceCharacter = '\0';
        var index = 0;
        while (index < line.Length && index < 3 && line[index] == ' ')
        {
            index++;
        }

        if (index + 2 >= line.Length || (line[index] != '`' && line[index] != '~'))
        {
            return false;
        }

        var character = line[index];
        if (line[index + 1] != character || line[index + 2] != character)
        {
            return false;
        }

        fenceCharacter = character;
        return true;
    }
}
