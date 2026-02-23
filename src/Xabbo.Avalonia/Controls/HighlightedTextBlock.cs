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
        TextProperty.Changed.AddClassHandler<HighlightedTextBlock>((x, _) => x.QueueUpdateInlines());
        HighlightWordsProperty.Changed.AddClassHandler<HighlightedTextBlock>((x, _) => x.QueueUpdateInlines());
        HighlightForegroundProperty.Changed.AddClassHandler<HighlightedTextBlock>((x, _) => x.QueueUpdateInlines());
        IsWhisperProperty.Changed.AddClassHandler<HighlightedTextBlock>((x, _) => x.QueueUpdateInlines());
        WhisperRecipientProperty.Changed.AddClassHandler<HighlightedTextBlock>((x, _) => x.QueueUpdateInlines());
    }

    private bool _updatePending;

    private void QueueUpdateInlines()
    {
        if (_updatePending) return;
        _updatePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _updatePending = false;
            UpdateInlines();
        }, DispatcherPriority.Normal);
    }

    private void UpdateInlines()
    {
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

            if (escapedWords.Count == 0)
            {
                var run = new Run(text);
                if (isWhisper) { run.FontStyle = FontStyle.Italic; run.Foreground = WhisperBrush; }
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
                        var run = new Run(text.Substring(currentIndex, match.Index - currentIndex));
                        if (isWhisper) { run.FontStyle = FontStyle.Italic; run.Foreground = WhisperBrush; }
                        runs.Add(run);
                    }

                    runs.Add(new Run(match.Value)
                    {
                        Foreground = HighlightForeground,
                        FontWeight = FontWeight.SemiBold
                    });

                    currentIndex = match.Index + match.Length;
                }

                if (currentIndex < text.Length)
                {
                    var run = new Run(text.Substring(currentIndex));
                    if (isWhisper) { run.FontStyle = FontStyle.Italic; run.Foreground = WhisperBrush; }
                    runs.Add(run);
                }
            }
        }

        Inlines?.Clear();
        if (runs.Count > 0)
            Inlines?.AddRange(runs);
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        UpdateInlines();
    }
}
