namespace WebApi.Services.Chat;

/// <summary>
/// A state object intended to be passed to plugins. Contains request-scoped data.
/// </summary>
public class AiState
{
    public Guid UserId { get; set; }
    public Guid? ConversationId { get; set; }
}
