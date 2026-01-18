using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

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

    static HighlightedTextBlock()
    {
        TextProperty.Changed.AddClassHandler<HighlightedTextBlock>((x, _) => x.UpdateInlines());
        HighlightWordsProperty.Changed.AddClassHandler<HighlightedTextBlock>((x, _) => x.UpdateInlines());
        HighlightForegroundProperty.Changed.AddClassHandler<HighlightedTextBlock>((x, _) => x.UpdateInlines());
    }

    private void UpdateInlines()
    {
        Inlines?.Clear();

        var text = Text;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var words = HighlightWords;
        if (words is null or { Count: 0 })
        {
            // No words to highlight, just show the text
            Inlines?.Add(new Run(text));
            return;
        }

        // Build a regex pattern to match any of the highlight words (case-insensitive)
        var escapedWords = new List<string>();
        foreach (var word in words)
        {
            if (!string.IsNullOrWhiteSpace(word))
            {
                escapedWords.Add(Regex.Escape(word));
            }
        }

        if (escapedWords.Count == 0)
        {
            Inlines?.Add(new Run(text));
            return;
        }

        var pattern = string.Join("|", escapedWords);
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        int currentIndex = 0;
        foreach (Match match in regex.Matches(text))
        {
            // Add text before the match
            if (match.Index > currentIndex)
            {
                Inlines?.Add(new Run(text.Substring(currentIndex, match.Index - currentIndex)));
            }

            // Add the highlighted match
            var highlightRun = new Run(match.Value)
            {
                Foreground = HighlightForeground,
                FontWeight = FontWeight.SemiBold
            };
            Inlines?.Add(highlightRun);

            currentIndex = match.Index + match.Length;
        }

        // Add remaining text after last match
        if (currentIndex < text.Length)
        {
            Inlines?.Add(new Run(text.Substring(currentIndex)));
        }
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        UpdateInlines();
    }
}
