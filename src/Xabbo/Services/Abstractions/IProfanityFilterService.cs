namespace Xabbo.Services.Abstractions;

/// <summary>
/// Represents a match of profanity in a message.
/// </summary>
public readonly record struct ProfanityMatch(int Start, int Length, string MatchedWord);

/// <summary>
/// Service for detecting profanity in messages with support for obfuscated words.
/// </summary>
public interface IProfanityFilterService
{
    /// <summary>
    /// Raised when profanity patterns are rebuilt (words added/removed).
    /// Not raised during initial construction.
    /// </summary>
    event Action? PatternsChanged;

    /// <summary>
    /// Checks if the message contains any profanity.
    /// </summary>
    bool ContainsProfanity(string message);

    /// <summary>
    /// Finds all profanity matches in the message.
    /// </summary>
    IReadOnlyList<ProfanityMatch> FindMatches(string message);

    /// <summary>
    /// Adds a word to the profanity filter.
    /// </summary>
    void AddWord(string word);

    /// <summary>
    /// Removes a word from the profanity filter.
    /// </summary>
    void RemoveWord(string word);

    /// <summary>
    /// Gets custom words added by the user.
    /// </summary>
    IReadOnlyList<string> GetCustomWords();

    /// <summary>
    /// Gets all configured words (default + custom).
    /// </summary>
    IReadOnlyList<string> GetAllWords();
}
