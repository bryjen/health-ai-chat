namespace Web.Common.DTOs.Conversations;

public class MessageDto
{
    public Guid Id { get; set; }
    public required string Role { get; set; }
    public required string Content { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? StatusInformationJson { get; set; } // JSON array of status information for assistant messages
}
