namespace Web.Common.DTOs.Health;

public class HealthChatRequest
{
    public required string Message { get; set; }
    public Guid? ConversationId { get; set; }
}
