namespace Xabbo.Models;

/// <summary>
/// Represents a selectable chat bubble style.
/// </summary>
public sealed record ChatBubbleOption(int Id, string Name)
{
    /// <summary>
    /// All known chat bubble options. Names will be updated once actual bubble images/names are identified.
    /// </summary>
    public static readonly IReadOnlyList<ChatBubbleOption> All =
    [
        new(0,  "Default"),
        new(1,  "Bubble 1"),
        new(2,  "Bubble 2"),
        new(3,  "Bubble 3"),
        new(4,  "Bubble 4"),
        new(5,  "Bubble 5"),
        new(6,  "Bubble 6"),
        new(7,  "Bubble 7"),
        new(8,  "Bubble 8"),
        new(9,  "Bubble 9"),
        new(10, "Bubble 10"),
        new(11, "Bubble 11"),
        new(12, "Bubble 12"),
        new(13, "Bubble 13"),
        new(14, "Bubble 14"),
        new(15, "Bubble 15"),
        new(16, "Bubble 16"),
        new(17, "Bubble 17"),
        new(18, "Bubble 18"),
        new(19, "Bubble 19"),
        new(20, "Bubble 20"),
        new(21, "Bubble 21"),
        new(22, "Bubble 22"),
        new(23, "Bubble 23"),
        new(24, "Bubble 24"),
        new(25, "Bubble 25"),
        new(26, "Bubble 26"),
        new(27, "Bubble 27"),
        new(28, "Bubble 28"),
        new(29, "Bubble 29"),
        new(30, "Bubble 30"),
        new(31, "Bubble 31"),
        new(32, "Bubble 32"),
        new(33, "Bubble 33"),
    ];

    public override string ToString() => Name;
}
