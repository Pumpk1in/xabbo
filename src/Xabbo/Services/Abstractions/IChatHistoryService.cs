using Xabbo.Models;

namespace Xabbo.Services.Abstractions;

public interface IChatHistoryService
{
    /// <summary>
    /// Adds a chat entry to the history (blocking).
    /// </summary>
    void AddEntry(ChatHistoryEntry entry);

    /// <summary>
    /// Enqueues a chat entry for async write to the history (non-blocking).
    /// </summary>
    void EnqueueEntry(ChatHistoryEntry entry);

    /// <summary>
    /// Searches the chat history with optional filters.
    /// </summary>
    IEnumerable<ChatHistoryEntry> Search(
        string? userName = null,
        string? keyword = null,
        bool? profanityOnly = null,
        bool? whispersOnly = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int? limit = null,
        int? offset = null);

    /// <summary>
    /// Searches the chat history and returns total count along with limited results.
    /// </summary>
    (IEnumerable<ChatHistoryEntry> Results, int TotalCount) SearchWithCount(
        string? userName = null,
        string? keyword = null,
        bool? profanityOnly = null,
        bool? whispersOnly = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int? limit = null,
        int? offset = null);

    /// <summary>
    /// Re-scans all messages and updates has_profanity flags in the database.
    /// </summary>
    Task UpdateProfanityFlagsAsync(IProfanityFilterService profanityFilter);

    /// <summary>
    /// Returns the 0-based position of an entry by timestamp ordered DESC.
    /// I.e., how many entries have a strictly newer timestamp.
    /// </summary>
    int GetEntryOffset(DateTime timestamp);

    /// <summary>
    /// Gets the total count of entries in the history.
    /// </summary>
    int GetEntryCount();

    /// <summary>
    /// Clears all history.
    /// </summary>
    void Clear();

    /// <summary>
    /// Saves the history to disk.
    /// </summary>
    Task SaveAsync();

    /// <summary>
    /// Loads the history from disk.
    /// </summary>
    Task LoadAsync();
}
