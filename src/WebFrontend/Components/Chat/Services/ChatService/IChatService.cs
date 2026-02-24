namespace WebFrontend.Components.Chat.Services.ChatService;

public interface IChatService
{
    string? ConversationId { get; set; }
    IAsyncEnumerable<string> RunStreamingAsync(string prompt, CancellationToken cancellationToken = default);
}
