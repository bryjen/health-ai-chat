namespace WebApi.Services.Chat;

/// <summary>
/// A state object intended to be passed to plugins. Contains request-scoped data.
/// </summary>
public class AiState
{
    public Guid UserId { get; set; } = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public Guid? ConversationId { get; set; } = Guid.Parse("00000000-0000-0000-0000-000000000002");
}
