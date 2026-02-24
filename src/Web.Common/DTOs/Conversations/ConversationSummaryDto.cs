namespace Web.Common.DTOs.Conversations;

public class ConversationSummaryDto
{
    public string Id { get; set; } = null!;
    public required string Title { get; set; }
    public string? LastMessagePreview { get; set; }
    public DateTime UpdatedAt { get; set; }
}
