namespace Xabbo.Models;

public sealed record ChatBubbleOption(int Id, string Name, bool IsOther = false)
{
    public static readonly IReadOnlyList<ChatBubbleOption> NormalBubbles =
    [
        new(0,  "Default"),
        new(3,  "Bubble 3"),
        new(4,  "Bubble 4"),
        new(5,  "Bubble 5"),
        new(6,  "Bubble 6"),
        new(7,  "Bubble 7"),
        new(9,  "Bubble 9"),
        new(10, "Bubble 10"),
        new(11, "Bubble 11"),
        new(12, "Bubble 12"),
        new(13, "Bubble 13"),
        new(14, "Bubble 14"),
        new(15, "Bubble 15"),
        new(16, "Bubble 16"),
        new(17, "Bubble 17"),
        new(19, "Bubble 19"),
        new(20, "Bubble 20"),
        new(21, "Bubble 21"),
        new(22, "Bubble 22"),
        new(24, "Bubble 24"),
        new(25, "Bubble 25"),
        new(26, "Bubble 26"),
        new(27, "Bubble 27"),
        new(29, "Bubble 29"),
    ];

    public static readonly IReadOnlyList<ChatBubbleOption> OtherBubbles =
    [
        new(1,  "Bubble 1",  IsOther: true),
        new(28, "Bubble 28", IsOther: true),
        new(30, "Bubble 30", IsOther: true),
        new(32, "Bubble 32", IsOther: true),
        new(35, "Bubble 35", IsOther: true),
        new(36, "Bubble 36", IsOther: true),
        new(37, "Bubble 37", IsOther: true),
        new(39, "Bubble 39", IsOther: true),
    ];

    public override string ToString() => Name;
}
