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
    public bool HasProfanity { get; init; }
    public List<string>? MatchedWords { get; init; }

    // For action type
    public string? UserName { get; init; }
    public string? Action { get; init; }

    // For room type
    public string? RoomName { get; init; }
    public string? RoomOwner { get; init; }

    /// <summary>
    /// Gets a formatted display text for this entry.
    /// </summary>
    [JsonIgnore]
    public string DisplayText => Type switch
    {
        "message" => $"{Name}: {Message}",
        "action" => $"{UserName} {Action}",
        "room" => $"Entered: {RoomName} by {RoomOwner}",
        _ => ToString() ?? ""
    };
}

[JsonSerializable(typeof(List<ChatHistoryEntry>))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal partial class ChatHistoryJsonContext : JsonSerializerContext
{
}
