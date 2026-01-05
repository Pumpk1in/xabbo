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

    private static readonly IBrush ProfanityBrush = new SolidColorBrush(Color.Parse("#FF4444"));
    private static readonly IBrush WhisperBrush = new SolidColorBrush(Color.Parse("#a495ff"));

    static ProfanityHighlightTextBlock()
    {
        SegmentsProperty.Changed.AddClassHandler<ProfanityHighlightTextBlock>((x, _) => x.UpdateInlines());
        FallbackTextProperty.Changed.AddClassHandler<ProfanityHighlightTextBlock>((x, _) => x.UpdateInlines());
        IsWhisperProperty.Changed.AddClassHandler<ProfanityHighlightTextBlock>((x, _) => x.UpdateInlines());
        UsernameProperty.Changed.AddClassHandler<ProfanityHighlightTextBlock>((x, _) => x.UpdateInlines());
    }

    private void UpdateInlines()
    {
        Inlines?.Clear();

        // Add username prefix if provided
        if (!string.IsNullOrEmpty(Username))
        {
            var usernameRun = new Run(Username)
            {
                FontWeight = FontWeight.Bold
            };
            if (IsWhisper)
            {
                usernameRun.Foreground = WhisperBrush;
            }
            Inlines?.Add(usernameRun);

            var separatorRun = new Run(": ");
            if (IsWhisper)
            {
                separatorRun.FontStyle = FontStyle.Italic;
                separatorRun.Foreground = WhisperBrush;
            }
            Inlines?.Add(separatorRun);
        }

        var segments = Segments;
        if (segments is null or { Count: 0 })
        {
            // No segments, use fallback text
            var fallbackRun = new Run(FallbackText ?? string.Empty);
            if (IsWhisper)
            {
                fallbackRun.FontStyle = FontStyle.Italic;
                fallbackRun.Foreground = WhisperBrush;
            }
            Inlines?.Add(fallbackRun);
            return;
        }

        foreach (var segment in segments)
        {
            var run = new Run(segment.Text);

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

            Inlines?.Add(run);
        }
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        UpdateInlines();
    }
}
