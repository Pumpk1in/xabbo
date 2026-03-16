using Xabbo.Core;

namespace Xabbo.Services.Abstractions;

public interface IPrivateMessageHistoryService
{
    /// <summary>
    /// Enqueues a message for async write (non-blocking).
    /// </summary>
    void Enqueue(Id friendId, string friendName, string senderName, string content, DateTimeOffset timestamp, bool isFromMe);

    /// <summary>
    /// Loads all conversations from the database.
    /// </summary>
    Task<IReadOnlyList<PrivateMessageRecord>> LoadAllAsync();

    /// <summary>
    /// Loads messages for a single friend from the database.
    /// </summary>
    Task<IReadOnlyList<PrivateMessageRecord>> LoadConversationAsync(Id friendId);

    /// <summary>
    /// Deletes all messages for a given friend from the database.
    /// </summary>
    Task DeleteConversationAsync(Id friendId);
}

public sealed record PrivateMessageRecord(
    Id FriendId,
    string FriendName,
    string SenderName,
    string Content,
    DateTimeOffset Timestamp,
    bool IsFromMe);
