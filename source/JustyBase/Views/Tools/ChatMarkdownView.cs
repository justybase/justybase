using System.Net;
using System.Text;
using JustyBase.Ai.Chat;
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
