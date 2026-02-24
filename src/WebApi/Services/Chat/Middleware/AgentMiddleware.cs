using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Spectre.Console;

#pragma warning disable MEAI001

namespace WebApi.Services.Chat.Middleware;

public static class AgentMiddleware
{
    // Function invocation middleware that logs before and after function calls.
    public static async ValueTask<object?> FunctionCallMiddleware(AIAgent agent, FunctionInvocationContext context, Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next, CancellationToken cancellationToken)
    {
        Log($"Called function: `{context.Function.Name}`");
        var result = await next(context, cancellationToken);

        return result;
    }

    // This middleware handles chat client lower level invocations.
    // This is useful for handling agent messages before they are sent to the LLM and also handle any response messages from the LLM before they are sent back to the agent.
    public static async Task<ChatResponse> ChatClientMiddleware(IEnumerable<ChatMessage> message, ChatOptions? options, IChatClient innerChatClient, CancellationToken cancellationToken)
    {
        var response = await innerChatClient.GetResponseAsync(message, options, cancellationToken);
        return response;
    }

    // Streaming middleware - must match signature expected by IChatClient.AsBuilder().Use()
    public static async IAsyncEnumerable<ChatResponseUpdate> ChatClientStreamingMiddleware(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        IChatClient innerChatClient,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        /*
        if (options?.Tools is { Count: > 0 })
        {
            var toolSchemas = options.Tools
                .OfType<AIFunction>()
                .Select((t, i) => new { index = i, name = t.Name, schema = t.JsonSchema })
                .ToList();
            System.Console.WriteLine("[TOOL SCHEMAS] " + JsonSerializer.Serialize(toolSchemas, new JsonSerializerOptions { WriteIndented = true }));
        }
         */

        await foreach (var update in innerChatClient.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            yield return update;
        }
        // Log("Chat Client Streaming Middleware - Post-Stream");
    }

    private static void Log(string message)
    {
        AnsiConsole.MarkupLine($"[grey]{message}[/]");
        System.Console.Out.Flush();
    }
}
