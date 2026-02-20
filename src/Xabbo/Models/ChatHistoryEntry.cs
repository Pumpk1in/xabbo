using System.Text.Json.Serialization;

namespace Xabbo.Models;

/// <summary>
/// Represents a chat message entry stored in the history.
/// </summary>
public sealed class ChatHistoryEntry
{
    public required DateTime Timestamp { get; init; }
    public required string Type { get; init; } // "message", "action", "room"

    // For message type
    public string? Name { get; init; }
    public string? Message { get; init; }
    public string? ChatType { get; init; }
    public bool IsWhisper { get; init; }
    public string? WhisperRecipient { get; init; }
    public bool HasProfanity { get; set; }
    public List<string>? MatchedWords { get; set; }

    // For action type
    public string? UserName { get; init; }
    public string? Action { get; init; }

    // For room type
    public string? RoomName { get; init; }
    public string? RoomOwner { get; init; }

    /// <summary>
    /// True if this is a message type entry (not action/room) and not from the local user.
    /// Set after retrieval when the local username is known.
    /// </summary>
    [JsonIgnore]
    public bool CanBan { get; set; }

    /// <summary>
    /// Gets a formatted display text for this entry.
    /// </summary>
    [JsonIgnore]
    public string DisplayText => Type switch
    {
        "message" when !string.IsNullOrEmpty(WhisperRecipient) => $"{Name} -> {WhisperRecipient}: {Message}",
        "message" => $"{Name}: {Message}",
        "action" => $"{UserName} {Action}",
        "room" => $"Entered: {RoomName} by {RoomOwner}",
        _ => ToString() ?? ""
    };
}

[JsonSerializable(typeof(List<ChatHistoryEntry>))]
[JsonSerializable(typeof(List<string>))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal partial class ChatHistoryJsonContext : JsonSerializerContext
{
}
