namespace WebFrontend.Components.Chat.Services.ChatService;

public interface IChatService
{
    IAsyncEnumerable<string> RunStreamingAsync(string prompt, CancellationToken cancellationToken = default);
}
