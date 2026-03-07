using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Xabbo.ViewModels;

namespace Xabbo.Avalonia.Controls;

/// <summary>
/// A TextBlock that highlights profanity segments in red with underline.
/// </summary>
public class ProfanityHighlightTextBlock : TextBlock
{
    public static readonly StyledProperty<IReadOnlyList<MessageSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<ProfanityHighlightTextBlock, IReadOnlyList<MessageSegment>?>(nameof(Segments));

    public static readonly StyledProperty<string?> FallbackTextProperty =
        AvaloniaProperty.Register<ProfanityHighlightTextBlock, string?>(nameof(FallbackText));

    public static readonly StyledProperty<bool> IsWhisperProperty =
        AvaloniaProperty.Register<ProfanityHighlightTextBlock, bool>(nameof(IsWhisper));

    public static readonly StyledProperty<string?> UsernameProperty =
        AvaloniaProperty.Register<ProfanityHighlightTextBlock, string?>(nameof(Username));

    public static readonly StyledProperty<string?> WhisperRecipientProperty =
        AvaloniaProperty.Register<ProfanityHighlightTextBlock, string?>(nameof(WhisperRecipient));

    /// <summary>
    /// The message segments to display with profanity highlighting.
    /// </summary>
    public IReadOnlyList<MessageSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    /// <summary>
    /// The fallback text to display when Segments is null or empty.
    /// </summary>
    public string? FallbackText
    {
        get => GetValue(FallbackTextProperty);
        set => SetValue(FallbackTextProperty, value);
    }

    /// <summary>
    /// Whether this is a whisper message (applies whisper styling).
    /// </summary>
    public bool IsWhisper
    {
        get => GetValue(IsWhisperProperty);
        set => SetValue(IsWhisperProperty, value);
    }

    /// <summary>
    /// The username to display before the message.
    /// </summary>
    public string? Username
    {
        get => GetValue(UsernameProperty);
        set => SetValue(UsernameProperty, value);
    }

    /// <summary>
    /// The whisper recipient name for outgoing whispers (displays "Name -> Recipient: msg").
    /// </summary>
    public string? WhisperRecipient
    {
        get => GetValue(WhisperRecipientProperty);
        set => SetValue(WhisperRecipientProperty, value);
    }

    private static readonly IBrush ProfanityBrush = new SolidColorBrush(Color.Parse("#FF4444"));
    private static readonly IBrush WhisperBrush = new SolidColorBrush(Color.Parse("#a495ff"));

    static ProfanityHighlightTextBlock()
    {
        SegmentsProperty.Changed.AddClassHandler<ProfanityHighlightTextBlock>((x, _) => x.UpdateInlines());
        FallbackTextProperty.Changed.AddClassHandler<ProfanityHighlightTextBlock>((x, _) => x.UpdateInlines());
        IsWhisperProperty.Changed.AddClassHandler<ProfanityHighlightTextBlock>((x, _) => x.UpdateInlines());
        UsernameProperty.Changed.AddClassHandler<ProfanityHighlightTextBlock>((x, _) => x.UpdateInlines());
        WhisperRecipientProperty.Changed.AddClassHandler<ProfanityHighlightTextBlock>((x, _) => x.UpdateInlines());
    }

    // Inserts zero-width spaces every N chars in runs with no natural break points,
    // preventing Avalonia's TextWrapping from entering an infinite layout loop.
    private static string InjectWordBreaks(string text, int every = 30)
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
        var runs = new List<Inline>();

        // Add username prefix if provided
        if (!string.IsNullOrEmpty(Username))
        {
            var usernameRun = new Run(InjectWordBreaks(Username))
            {
                FontWeight = FontWeight.Bold
            };
            if (IsWhisper)
                usernameRun.Foreground = WhisperBrush;
            runs.Add(usernameRun);

            // For outgoing whispers, show "Name -> Recipient: msg" (all italic/purple, no bold)
            if (!string.IsNullOrEmpty(WhisperRecipient))
            {
                runs.Add(new Run($" -> {InjectWordBreaks(WhisperRecipient)}")
                {
                    FontStyle = FontStyle.Italic,
                    Foreground = WhisperBrush
                });
            }

            var separatorRun = new Run(": ");
            if (IsWhisper)
            {
                separatorRun.FontStyle = FontStyle.Italic;
                separatorRun.Foreground = WhisperBrush;
            }
            runs.Add(separatorRun);
        }

        var segments = Segments;
        if (segments is null or { Count: 0 })
        {
            var fallbackRun = new Run(InjectWordBreaks(FallbackText ?? string.Empty));
            if (IsWhisper)
            {
                fallbackRun.FontStyle = FontStyle.Italic;
                fallbackRun.Foreground = WhisperBrush;
            }
            runs.Add(fallbackRun);
        }
        else
        {
            foreach (var segment in segments)
            {
                var run = new Run(InjectWordBreaks(segment.Text));

                if (segment.IsProfanity)
                {
                    run.Foreground = ProfanityBrush;
                    run.TextDecorations = global::Avalonia.Media.TextDecorations.Underline;
                    run.FontWeight = FontWeight.SemiBold;
                }
                else if (IsWhisper)
                {
                    run.FontStyle = FontStyle.Italic;
                    run.Foreground = WhisperBrush;
                }

                runs.Add(run);
            }
        }

        Inlines?.Clear();
        Inlines?.AddRange(runs);
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        UpdateInlines();
    }
}
