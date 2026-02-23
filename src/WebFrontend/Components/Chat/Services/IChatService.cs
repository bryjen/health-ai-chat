namespace WebFrontend.Components.Chat.Services;

public interface IChatService
{
    IAsyncEnumerable<string> RunStreamingAsync(string prompt, CancellationToken cancellationToken = default);
}
