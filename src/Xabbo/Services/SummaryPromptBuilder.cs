using System.Text;

using Xabbo.Models;

namespace Xabbo.Services;

public static class SummaryPromptBuilder
{
    public static string BuildUserPrompt(IEnumerable<ChatHistoryEntry> messages, string? instructionReminder = null)
    {
        var sb = new StringBuilder();

        sb.AppendLine("=== CHAT LOG ===");
        sb.AppendLine();

        var groups = messages
            .GroupBy(m => (m.RoomId, RoomName: m.RoomName ?? "(unknown room)"))
            .OrderBy(g => g.Min(m => m.Timestamp));

        foreach (var group in groups)
        {
            sb.Append("## Room: ").AppendLine(group.Key.RoomName);

            foreach (var entry in group.OrderBy(m => m.Timestamp))
            {
                sb.Append('[').Append(entry.Timestamp.ToString("HH:mm")).Append("] ");

                switch (entry.Type)
                {
                    case "message":
                        if (!string.IsNullOrEmpty(entry.WhisperRecipient))
                            sb.Append(entry.Name).Append(" -> ").Append(entry.WhisperRecipient).Append(": ");
                        else
                            sb.Append(entry.Name).Append(": ");
                        sb.AppendLine(entry.Message);
                        break;

                    case "action":
                        sb.Append(entry.UserName).Append(' ').AppendLine(entry.Action);
                        break;

                    case "room":
                        sb.Append("Entered: ").Append(entry.RoomName).Append(" by ").AppendLine(entry.RoomOwner);
                        break;

                    default:
                        sb.AppendLine(entry.DisplayText);
                        break;
                }
            }

            sb.AppendLine();
        }

        sb.AppendLine("=== END CHAT LOG ===");

        if (!string.IsNullOrWhiteSpace(instructionReminder))
        {
            sb.AppendLine();
            sb.AppendLine("=== INSTRUCTIONS REMINDER (apply these strictly) ===");
            sb.AppendLine(instructionReminder);
            sb.AppendLine("=== END REMINDER ===");
        }

        return sb.ToString();
    }
}
