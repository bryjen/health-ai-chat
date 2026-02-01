using WebFrontend.Models.Chat;

namespace WebFrontend.Pages;

public class ChatMessage
{
    public string Content { get; set; } = string.Empty;
    public bool IsUser { get; set; }
    public DateTime Timestamp { get; set; }
    public List<StatusInformation> StatusInformation { get; set; } = new();
}

