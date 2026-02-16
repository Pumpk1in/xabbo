namespace Xabbo.Models;

public class WhisperSuggestionItem
{
    public string Name { get; init; } = "";
    public bool IsInRoom { get; set; }
    public bool IsRecent { get; set; }
}
