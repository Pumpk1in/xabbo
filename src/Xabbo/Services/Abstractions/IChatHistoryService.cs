using Xabbo.Models;

namespace Xabbo.Services.Abstractions;

public interface IChatHistoryService
{
    /// <summary>
    /// Adds a chat entry to the history.
    /// </summary>
    void AddEntry(ChatHistoryEntry entry);

    /// <summary>
    /// Searches the chat history with optional filters.
    /// </summary>
    IEnumerable<ChatHistoryEntry> Search(
        string? userName = null,
        string? keyword = null,
        bool? profanityOnly = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int? limit = null);

    /// <summary>
    /// Searches the chat history and returns total count along with limited results.
    /// </summary>
    (IEnumerable<ChatHistoryEntry> Results, int TotalCount) SearchWithCount(
        string? userName = null,
        string? keyword = null,
        bool? profanityOnly = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int? limit = null);

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
