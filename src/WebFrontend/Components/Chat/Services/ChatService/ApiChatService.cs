using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using WebApi.ApiWrapper.Services;

namespace WebFrontend.Components.Chat.Services.ChatService;

public class ApiChatService(IConversationsApiClient conversationsApiClient) : IChatService
{
    private string? _conversationId;

    public string? ConversationId
    {
        get => _conversationId;
        set => _conversationId = value;
    }

    public async IAsyncEnumerable<string> RunStreamingAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // The backend streams raw text chunks. The very last chunk (or last line)
        // contains {"type":"complete","conversationId":"..."} which we intercept.
        // We buffer only what's needed to detect that terminal JSON line.
        var tail = new StringBuilder();

        await foreach (var chunk in conversationsApiClient.SendMessageStreamAsync(prompt, _conversationId, cancellationToken))
        {
            // Append to tail buffer to detect the complete message
            tail.Append(chunk);

            // Check if tail contains the complete JSON line
            var tailStr = tail.ToString();
            var completeIdx = tailStr.IndexOf("{\"type\":\"complete\"", StringComparison.Ordinal);

            if (completeIdx >= 0)
            {
                // Yield everything before the complete marker
                if (completeIdx > 0)
                    yield return tailStr[..completeIdx];

                // Extract conversationId from the complete line
                var jsonPart = tailStr[completeIdx..];
                var newlineIdx = jsonPart.IndexOf('\n');
                var jsonLine = newlineIdx >= 0 ? jsonPart[..newlineIdx] : jsonPart;
                TryExtractConversationId(jsonLine);
                break;
            }

            // Keep only last 200 chars in tail to detect the complete marker spanning chunks
            // but yield everything except that tail
            if (tail.Length > 200)
            {
                var toYield = tail.ToString()[..^200];
                tail.Remove(0, toYield.Length);
                yield return toYield;
            }
        }
    }

    private void TryExtractConversationId(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("conversationId", out var idElement))
                _conversationId = idElement.GetString();
        }
        catch (JsonException) { }
    }
}
