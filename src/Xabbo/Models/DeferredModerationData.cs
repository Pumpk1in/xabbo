namespace Xabbo.Models;

public sealed class DeferredModerationData
{
    public Dictionary<long, Dictionary<string, int>> Bans { get; set; } = [];
    public Dictionary<long, Dictionary<string, int>> Mutes { get; set; } = [];
}
