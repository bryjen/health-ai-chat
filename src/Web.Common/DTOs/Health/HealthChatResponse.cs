namespace Web.Common.DTOs.Health;

public class HealthChatResponse
{
    public required string Message { get; set; }
    public required Guid ConversationId { get; set; }
    public List<EntityChange> SymptomChanges { get; set; } = new();
    public List<EntityChange> AppointmentChanges { get; set; } = new();
}
