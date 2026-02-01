namespace Web.Common.DTOs.Conversations;

public class ConversationDto
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public List<MessageDto> Messages { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
