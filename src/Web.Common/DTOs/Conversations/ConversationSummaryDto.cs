namespace Web.Common.DTOs.Conversations;

public class ConversationSummaryDto
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public string? LastMessagePreview { get; set; }
    public DateTime UpdatedAt { get; set; }
}
