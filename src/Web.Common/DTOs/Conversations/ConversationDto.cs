namespace Web.Common.DTOs.Conversations;

public class ConversationDto
{
    public string Id { get; set; } = null!;
    public string? Title { get; set; }
    public List<MessageDto> Messages { get; set; } = new();
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
