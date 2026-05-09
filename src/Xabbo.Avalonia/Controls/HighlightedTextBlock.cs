using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;

namespace Xabbo.Avalonia.Controls;

/// <summary>
/// A TextBlock that highlights specific words in a different color.
/// </summary>
public class HighlightedTextBlock : TextBlock
{
    public static new readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<HighlightedTextBlock, string?>(nameof(Text));

    public static readonly StyledProperty<IReadOnlyList<string>?> HighlightWordsProperty =
        AvaloniaProperty.Register<HighlightedTextBlock, IReadOnlyList<string>?>(nameof(HighlightWords));

    public static readonly StyledProperty<IBrush?> HighlightForegroundProperty =
        AvaloniaProperty.Register<HighlightedTextBlock, IBrush?>(nameof(HighlightForeground),
            new SolidColorBrush(Color.Parse("#FF6B6B")));

    public static readonly StyledProperty<bool> IsWhisperProperty =
        AvaloniaProperty.Register<HighlightedTextBlock, bool>(nameof(IsWhisper));

    public static readonly StyledProperty<string?> WhisperRecipientProperty =
        AvaloniaProperty.Register<HighlightedTextBlock, string?>(nameof(WhisperRecipient));

    /// <summary>
    /// The text to display.
    /// </summary>
    public new string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// The words to highlight in the text.
    /// </summary>
    public IReadOnlyList<string>? HighlightWords
    {
        get => GetValue(HighlightWordsProperty);
        set => SetValue(HighlightWordsProperty, value);
    }

    /// <summary>
    /// The brush used for highlighted words.
    /// </summary>
    public IBrush? HighlightForeground
    {
        get => GetValue(HighlightForegroundProperty);
        set => SetValue(HighlightForegroundProperty, value);
    }

    /// <summary>
    /// Whether this is a whisper message (applies italic/purple styling).
    /// </summary>
    public bool IsWhisper
    {
        get => GetValue(IsWhisperProperty);
        set => SetValue(IsWhisperProperty, value);
    }

    /// <summary>
    /// The recipient name for outgoing whispers.
    /// </summary>
    public string? WhisperRecipient
    {
        get => GetValue(WhisperRecipientProperty);
        set => SetValue(WhisperRecipientProperty, value);
    }

    private static readonly IBrush WhisperBrush = new SolidColorBrush(Color.Parse("#a495ff"));

    static HighlightedTextBlock()
    {
        TextProperty.Changed.AddClassHandler<HighlightedTextBlock>((x, _) => x.ScheduleUpdateInlines());
        HighlightWordsProperty.Changed.AddClassHandler<HighlightedTextBlock>((x, _) => x.ScheduleUpdateInlines());
        HighlightForegroundProperty.Changed.AddClassHandler<HighlightedTextBlock>((x, _) => x.ScheduleUpdateInlines());
        IsWhisperProperty.Changed.AddClassHandler<HighlightedTextBlock>((x, _) => x.ScheduleUpdateInlines());
        WhisperRecipientProperty.Changed.AddClassHandler<HighlightedTextBlock>((x, _) => x.ScheduleUpdateInlines());
    }

    private bool _updatePending;
    private (string? text, IReadOnlyList<string>? words, IBrush? brush, bool whisper, string? recipient) _lastState;

    private void ScheduleUpdateInlines()
    {
        if (_updatePending) return;
        _updatePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _updatePending = false;
            UpdateInlines();
        }, DispatcherPriority.Loaded);
    }

    // Inserts zero-width spaces every N chars in runs with no natural break points,
    // preventing Avalonia's TextWrapping from entering an infinite layout loop.
    private static string InjectWordBreaks(string text, int every = 10)
    {
        if (text.Length <= every || text.Contains(' ')) return text;
        var sb = new System.Text.StringBuilder(text.Length + text.Length / every);
        for (int i = 0; i < text.Length; i++)
        {
            sb.Append(text[i]);
            if ((i + 1) % every == 0 && i + 1 < text.Length)
                sb.Append('\u200B');
        }
        return sb.ToString();
    }

    private void UpdateInlines()
    {
        var state = (Text, HighlightWords, HighlightForeground, IsWhisper, WhisperRecipient);
        if (state == _lastState) return;
        _lastState = state;

        var runs = new List<Inline>();

        var text = Text;
        if (!string.IsNullOrEmpty(text))
        {
            var isWhisper = IsWhisper;

            var words = HighlightWords;
            var escapedWords = new List<string>();
            if (words is not null)
            {
                foreach (var word in words)
                {
                    if (!string.IsNullOrWhiteSpace(word))
                        escapedWords.Add(Regex.Escape(word));
                }
            }

            // Whisper messages use WhisperBrush color but NOT FontStyle.Italic — italic forces Skia
            // to generate per-instance native TextLayouts that retain large amounts of native memory
            // and never get released. Color-only is sufficient to distinguish whispers visually.
            if (escapedWords.Count == 0)
            {
                var run = new Run(InjectWordBreaks(text));
                if (isWhisper) run.Foreground = WhisperBrush;
                runs.Add(run);
            }
            else
            {
                var pattern = string.Join("|", escapedWords);
                var regex = new Regex(pattern, RegexOptions.IgnoreCase);

                int currentIndex = 0;
                foreach (Match match in regex.Matches(text))
                {
                    if (match.Index > currentIndex)
                    {
                        var run = new Run(InjectWordBreaks(text.Substring(currentIndex, match.Index - currentIndex)));
                        if (isWhisper) run.Foreground = WhisperBrush;
                        runs.Add(run);
                    }

                    runs.Add(new Run(InjectWordBreaks(match.Value))
                    {
                        Foreground = HighlightForeground,
                        FontWeight = FontWeight.SemiBold
                    });

                    currentIndex = match.Index + match.Length;
                }

                if (currentIndex < text.Length)
                {
                    var run = new Run(InjectWordBreaks(text.Substring(currentIndex)));
                    if (isWhisper) run.Foreground = WhisperBrush;
                    runs.Add(run);
                }
            }
        }

        _measureDirty = true;
        Inlines?.Clear();
        if (runs.Count > 0)
            Inlines?.AddRange(runs);
    }

    // Cache measured size to prevent redundant text wrapping calculations
    // across consecutive layout passes with the same available width.
    private double _lastMeasureWidth = -1;
    private Size _lastMeasureResult;
    private bool _measureDirty = true;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!_measureDirty && Math.Abs(availableSize.Width - _lastMeasureWidth) < 3.0)
            return _lastMeasureResult;

        _measureDirty = false;
        _lastMeasureWidth = availableSize.Width;
        _lastMeasureResult = base.MeasureOverride(availableSize);
        return _lastMeasureResult;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        UpdateInlines();
    }
}
