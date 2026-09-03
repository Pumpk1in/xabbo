namespace Xabbo.Models;

public sealed class DeferredModerationData
{
    public Dictionary<long, Dictionary<string, int>> Bans { get; set; } = [];
    public Dictionary<long, Dictionary<string, int>> Mutes { get; set; } = [];
    // Per-room auto-ban glob patterns (case-insensitive) and the single ban duration applied to matches.
    public Dictionary<long, List<string>> AutoBanPatterns { get; set; } = [];
    public Dictionary<long, int> AutoBanDuration { get; set; } = [];
}
