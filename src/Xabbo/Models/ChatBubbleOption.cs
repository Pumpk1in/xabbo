namespace Xabbo.Models;

public enum BubbleCategory { Normal, Hc, Nft }

public sealed record ChatBubbleOption(int Id, string Name, BubbleCategory Category = BubbleCategory.Normal)
{
    public static readonly IReadOnlyList<ChatBubbleOption> NormalBubbles =
    [
        new(0,  "Normal"),
        new(3,  "Red"),
        new(4,  "Blue"),
        new(5,  "Yellow"),
        new(6,  "Green"),
        new(7,  "Grey"),
        new(37, "Ambassador"),
    ];

    public static readonly IReadOnlyList<ChatBubbleOption> HcBubbles =
    [
        new(9,  "Zombie Hand",     BubbleCategory.Hc),
        new(10, "Skeleton",        BubbleCategory.Hc),
        new(11, "Sky Blue",        BubbleCategory.Hc),
        new(12, "Pink",            BubbleCategory.Hc),
        new(13, "Purple",          BubbleCategory.Hc),
        new(14, "Dark Yellow",     BubbleCategory.Hc),
        new(15, "Dark Turquoise",  BubbleCategory.Hc),
        new(16, "Hearts",          BubbleCategory.Hc),
        new(17, "Gothic Rose",     BubbleCategory.Hc),
        new(19, "Piglet",          BubbleCategory.Hc),
        new(20, "Sausage Dog",     BubbleCategory.Hc),
        new(21, "Firing My Lazer", BubbleCategory.Hc),
        new(22, "Dragon",          BubbleCategory.Hc),
        new(24, "Bats",            BubbleCategory.Hc),
        new(25, "Console",         BubbleCategory.Hc),
        new(26, "Steampunk Pipe",  BubbleCategory.Hc),
        new(27, "Storm",           BubbleCategory.Hc),
        new(29, "Pirate",          BubbleCategory.Hc),
    ];

    public static readonly IReadOnlyList<ChatBubbleOption> NftBubbles =
    [
        new(1000, "Avatar Bronze",       BubbleCategory.Nft),
        new(1001, "Avatar Gold",         BubbleCategory.Nft),
        new(1002, "Avatar Diamond",      BubbleCategory.Nft),
        new(1003, "Avatar Rainbow",      BubbleCategory.Nft),
        new(1004, "Avatar Trippy",       BubbleCategory.Nft),
        new(1005, "Avatar Ultra Trippy", BubbleCategory.Nft),
        new(1006, "MVHQ",                BubbleCategory.Nft),
        new(1007, "Metakey",             BubbleCategory.Nft),
        new(1010, "Crafted Avatar",      BubbleCategory.Nft),
        new(1011, "Balloon Orange",      BubbleCategory.Nft),
        new(1012, "Balloon Blue",        BubbleCategory.Nft),
        new(1013, "Origami Orange",      BubbleCategory.Nft),
        new(1014, "Origami Blue",        BubbleCategory.Nft),
        new(1015, "Chocolate Dark",      BubbleCategory.Nft),
        new(1016, "Chocolate White",     BubbleCategory.Nft),
        new(1017, "Clay",                BubbleCategory.Nft),
        new(1018, "Scroll",              BubbleCategory.Nft),
        new(1019, "Pillow",              BubbleCategory.Nft),
        new(1020, "Bobba",               BubbleCategory.Nft),
        new(1021, "Pink Tube",           BubbleCategory.Nft),
        new(1022, "Keycaps",             BubbleCategory.Nft),
        new(1023, "Xmas 22",             BubbleCategory.Nft),
        new(1024, "Rocky",               BubbleCategory.Nft),
        new(1025, "Ice",                 BubbleCategory.Nft),
        new(1026, "Aurora",              BubbleCategory.Nft),
        new(1027, "Money",               BubbleCategory.Nft),
    ];

    public override string ToString() => Name;
}
