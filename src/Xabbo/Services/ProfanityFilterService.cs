using System.Collections.Specialized;
using System.Text;
using System.Text.RegularExpressions;
using Xabbo.Configuration;
using Xabbo.Services.Abstractions;

namespace Xabbo.Services;

/// <summary>
/// Service for detecting profanity in messages with support for obfuscated words.
/// Handles common obfuscation techniques like: cxnne, c0nne, c*nne, etc.
/// </summary>
public partial class ProfanityFilterService : IProfanityFilterService
{
    private readonly IConfigProvider<AppConfig> _configProvider;
    private ProfanityConfig Config => _configProvider.Value.Profanity;

    // Cache of compiled regex patterns for each word
    private readonly Dictionary<string, Regex> _patterns = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    // Track the current CustomWords collection to handle resubscription
    private System.Collections.ObjectModel.ObservableCollection<string>? _subscribedCollection;

    // Character substitutions for obfuscation detection
    private static readonly Dictionary<char, string> CharacterPatterns = new()
    {
        ['a'] = "[a@4àáâãäåx]",
        ['b'] = "[b8ß]",
        ['c'] = "[c¢©]",
        ['d'] = "[d]",
        ['e'] = "[e3€èéêëx]",
        ['f'] = "[f]",
        ['g'] = "[g9]",
        ['h'] = "[h]",
        ['i'] = "[i1!|ìíîïx]",
        ['j'] = "[j]",
        ['k'] = "[k]",
        ['l'] = "[l1|]",
        ['m'] = "[m]",
        ['n'] = "[nñ]",
        ['o'] = "[o0°òóôõöx]",
        ['p'] = "[p]",
        ['q'] = "[q]",
        ['r'] = "[r]",
        ['s'] = "[s5$§]",
        ['t'] = "[t7+]",
        ['u'] = "[uùúûüx]",
        ['v'] = "[v]",
        ['w'] = "[w]",
        ['x'] = "[x]",
        ['y'] = "[y¥]",
        ['z'] = "[z2]",
    };

    // Characters commonly used as fillers between letters
    private const string FillerPattern = @"[x*_\-.\s]*";

    public ProfanityFilterService(IConfigProvider<AppConfig> configProvider)
    {
        _configProvider = configProvider;

        // Build initial patterns and subscribe to changes
        EnsureSubscribed();
        RebuildPatterns();

        // Re-subscribe when config is reloaded (Value is replaced with new instance)
        _configProvider.Loaded += () =>
        {
            _subscribedCollection = null; // Force re-subscription
            EnsureSubscribed();
            RebuildPatterns();
        };
    }

    private void EnsureSubscribed()
    {
        var currentCollection = Config.CustomWords;
        if (_subscribedCollection != currentCollection)
        {
            // Unsubscribe from old collection
            if (_subscribedCollection != null)
            {
                _subscribedCollection.CollectionChanged -= OnCustomWordsChanged;
            }

            // Subscribe to new collection
            _subscribedCollection = currentCollection;
            _subscribedCollection.CollectionChanged += OnCustomWordsChanged;
        }
    }

    private void OnCustomWordsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildPatterns();
    }

    private void RebuildPatterns()
    {
        // Ensure we're subscribed to the current collection
        EnsureSubscribed();

        lock (_lock)
        {
            _patterns.Clear();
            // Build patterns for all words (default + custom)
            foreach (var word in Config.AllWords)
            {
                if (!string.IsNullOrWhiteSpace(word))
                {
                    _patterns[word] = BuildPattern(word);
                }
            }
        }
    }

    /// <summary>
    /// Builds a regex pattern for a word that matches obfuscated variants.
    /// Example: "conne" becomes pattern matching "conne", "cxnne", "c0nne", "c*nne", etc.
    /// </summary>
    private static Regex BuildPattern(string word)
    {
        var sb = new StringBuilder();
        sb.Append(@"\b"); // Word boundary at start

        for (int i = 0; i < word.Length; i++)
        {
            char c = char.ToLowerInvariant(word[i]);

            // Handle spaces - match one or more whitespace characters
            if (char.IsWhiteSpace(c))
            {
                sb.Append(@"\s+");
                continue;
            }

            // Add character class for this letter
            if (CharacterPatterns.TryGetValue(c, out var pattern))
            {
                sb.Append(pattern);
            }
            else
            {
                // Escape special regex characters
                sb.Append(Regex.Escape(c.ToString()));
            }

            // Add optional filler between characters (except after last char and before space)
            if (i < word.Length - 1 && !char.IsWhiteSpace(word[i + 1]))
            {
                sb.Append(FillerPattern);
            }
        }

        sb.Append(@"\b"); // Word boundary at end

        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    public bool ContainsProfanity(string message)
    {
        if (!Config.Enabled || string.IsNullOrEmpty(message))
            return false;

        lock (_lock)
        {
            foreach (var pattern in _patterns.Values)
            {
                if (pattern.IsMatch(message))
                    return true;
            }
        }

        return false;
    }

    public IReadOnlyList<ProfanityMatch> FindMatches(string message)
    {
        var matches = new List<ProfanityMatch>();

        if (!Config.Enabled || string.IsNullOrEmpty(message))
            return matches;

        lock (_lock)
        {
            foreach (var (word, pattern) in _patterns)
            {
                foreach (Match match in pattern.Matches(message))
                {
                    // Store the base word from the filter, not the obfuscated text
                    matches.Add(new ProfanityMatch(match.Index, match.Length, word));
                }
            }
        }

        // Sort by position and remove overlaps (keep longer matches)
        matches.Sort((a, b) => a.Start.CompareTo(b.Start));

        // Remove overlapping matches (keep the first/longer one)
        for (int i = matches.Count - 1; i > 0; i--)
        {
            var current = matches[i];
            var previous = matches[i - 1];

            // If current starts before previous ends, they overlap
            if (current.Start < previous.Start + previous.Length)
            {
                matches.RemoveAt(i);
            }
        }

        return matches;
    }

    public void AddWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return;

        word = word.Trim().ToLowerInvariant();

        // Check if already exists in default or custom words
        if (ProfanityConfig.DefaultWords.Contains(word, StringComparer.OrdinalIgnoreCase))
            return; // Already in defaults, no need to add

        if (!Config.CustomWords.Contains(word, StringComparer.OrdinalIgnoreCase))
        {
            Config.CustomWords.Add(word);
        }
    }

    public void RemoveWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return;

        // Only allow removing custom words, not default ones
        var existingWord = Config.CustomWords.FirstOrDefault(w =>
            w.Equals(word, StringComparison.OrdinalIgnoreCase));

        if (existingWord != null)
        {
            Config.CustomWords.Remove(existingWord);
        }
    }

    public IReadOnlyList<string> GetCustomWords() => Config.CustomWords.ToList();

    public IReadOnlyList<string> GetAllWords() => Config.AllWords.ToList();
}
