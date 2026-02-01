namespace Web.Common.DTOs.AI;

/// <summary>
/// Request DTO for the health chat scenario.
/// </summary>
public class HealthChatScenarioRequest
{
    /// <summary>
    /// The user's message to process.
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// Optional conversation ID for context. If provided, previous messages from this conversation will be included.
    /// </summary>
    public Guid? ConversationId { get; set; }

    /// <summary>
    /// The user ID making the request.
    /// </summary>
    public required Guid UserId { get; set; }
}
