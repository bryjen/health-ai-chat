using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Spectre.Console;
using WebApi.Services.Chat.Plugins;
using WebApi.Services.Chat.Response;

#pragma warning disable MEAI001

namespace WebApi.Services.Chat.Middleware;

public class AgentMiddleware(ResponseWriter responseWriter)
{
    // Function invocation middleware that logs before/after function calls and emits to response stream.
    public async ValueTask<object?> FunctionCallMiddleware(AIAgent agent, FunctionInvocationContext context, Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next, CancellationToken cancellationToken)
    {
        await responseWriter.EmitRawAsync("ToolCall", ToKebabCase(context.Function.Name), cancellationToken);
        var result = await next(context, cancellationToken);
        return result;
    }

    // This middleware handles chat client lower level invocations.
    public static async Task<ChatResponse> ChatClientMiddleware(IEnumerable<ChatMessage> message, ChatOptions? options, IChatClient innerChatClient, CancellationToken cancellationToken)
    {
        var response = await innerChatClient.GetResponseAsync(message, options, cancellationToken);
        return response;
    }

    // Streaming middleware
    public static async IAsyncEnumerable<ChatResponseUpdate> ChatClientStreamingMiddleware(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        IChatClient innerChatClient,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var update in innerChatClient.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            yield return update;
        }
    }

    private static string ToKebabCase(string s) =>
        Regex.Replace(s, "(?<=.)([A-Z])", "-$1").ToLower();
}
