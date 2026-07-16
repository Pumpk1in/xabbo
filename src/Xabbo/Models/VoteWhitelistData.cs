namespace Xabbo.Models;

public sealed class VoteWhitelistData
{
    public HashSet<long> Ids { get; set; } = [];
    public List<string> Names { get; set; } = [];
}
