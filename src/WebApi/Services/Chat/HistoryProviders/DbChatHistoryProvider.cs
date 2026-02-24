using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using WebApi.Models.EfCore.Chat;
using WebApi.Data;
using WebApi.Services.Chat.Response;

#pragma warning disable MEAI001

namespace WebApi.Services.Chat.HistoryProviders;

public sealed class DbChatHistoryProvider : ChatHistoryProvider
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ResponseWriter _responseWriter;

    public DbChatHistoryProvider(IServiceScopeFactory serviceScopeFactory, ResponseWriter responseWriter, JsonElement serializedState)
    {
        _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
        _responseWriter = responseWriter;

        if (serializedState.ValueKind is JsonValueKind.String)
        {
            SessionDbKey = serializedState.Deserialize<string>();
        }
    }

    public string? SessionDbKey { get; private set; }

    public override async ValueTask<IEnumerable<ChatMessage>> InvokingAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Load more messages to ensure tool use/result pairs aren't split
        // Increased from 10 to 50 to handle conversations with multiple tool calls
        var records = await dbContext.ChatMessages
            .Where(x => x.SessionId == SessionDbKey)
            .OrderByDescending(x => x.Timestamp)
            .Take(50)
            .ToListAsync(cancellationToken);

        records.Reverse();
        var messages = records.ConvertAll(x => JsonSerializer.Deserialize<ChatMessage>(x.SerializedMessage!)!);

        // Validate tool use/result pairing to prevent Anthropic API errors
        ValidateToolMessagePairing(messages);

        return messages;
    }

    private void ValidateToolMessagePairing(List<ChatMessage> messages)
    {
        // Check for orphaned tool results (results without corresponding tool use)
        for (int i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            var toolResults = message.Contents.OfType<FunctionResultContent>().ToList();

            if (toolResults.Any() && i > 0)
            {
                // Previous message should contain tool uses
                var prevMessage = messages[i - 1];
                var toolUses = prevMessage.Contents.OfType<FunctionCallContent>().ToList();

                // Verify each result has a corresponding use
                foreach (var result in toolResults)
                {
                    var hasMatchingUse = toolUses.Any(use => use.CallId == result.CallId);
                    if (!hasMatchingUse)
                    {
                        throw new InvalidOperationException(
                            $"Tool result with CallId '{result.CallId}' has no corresponding tool use. " +
                            "This indicates corrupted session state. Consider starting a new session.");
                    }
                }
            }
            else if (toolResults.Any() && i == 0)
            {
                throw new InvalidOperationException(
                    "First message in history contains tool results without preceding tool use. " +
                    "Session history is incomplete. Consider starting a new session.");
            }
        }
    }

    public override async ValueTask InvokedAsync(
        InvokedContext context, CancellationToken cancellationToken = default)
    {
        if (context.InvokeException is not null)
            return;

        SessionDbKey ??= Guid.NewGuid().ToString("N");

        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Save ALL messages including tool calls/results
        // Only filter out approval request/response (human-in-the-loop UI prompts)
        // FunctionCallContent and FunctionResultContent MUST be saved for Anthropic API compatibility
        var persistableMessages = context.RequestMessages
            .Concat(context.AIContextProviderMessages ?? [])
            .Concat(context.ResponseMessages ?? [])
            .Where(m => !m.Contents.Any(c => c is FunctionApprovalRequestContent or FunctionApprovalResponseContent))
            .ToList();

        // Drain any out-of-band tags emitted during plugin execution this turn.
        // We'll attach them to the last assistant entity after the save loop.
        var emittedRaw = _responseWriter.DrainEmittedRaw();
        var lastAssistantKey = (string?)null;

        foreach (var message in persistableMessages)
        {
            var messageId = message.MessageId
                ?? $"{message.Role}-{Convert.ToHexString(System.Security.Cryptography.MD5.HashData(
                    System.Text.Encoding.UTF8.GetBytes(message.Text ?? string.Empty)))}";
            var key = SessionDbKey + messageId;
            var existing = await dbContext.ChatMessages.FindAsync([key], cancellationToken);

            // Log what we're saving for debugging
            var hasToolCall = message.Contents.Any(c => c is FunctionCallContent);
            var hasToolResult = message.Contents.Any(c => c is FunctionResultContent);

            if (existing is not null)
            {
                existing.Timestamp = DateTime.UtcNow;
                existing.SerializedMessage = JsonSerializer.Serialize(message);
                existing.MessageText = message.Text;
            }
            else
            {
                dbContext.ChatMessages.Add(new ChatMessageEntity
                {
                    Key = key,
                    Timestamp = DateTime.UtcNow,
                    SessionId = SessionDbKey,
                    SerializedMessage = JsonSerializer.Serialize(message),
                    MessageText = message.Text
                });
            }

            if (message.Role == ChatRole.Assistant)
                lastAssistantKey = key;
        }

        // Attach emitted tags to the last assistant message entity so they're available on replay.
        if (emittedRaw.Count > 0 && lastAssistantKey is not null)
        {
            var entity = await dbContext.ChatMessages.FindAsync([lastAssistantKey], cancellationToken);
            if (entity is not null)
                entity.EmittedTags = string.Concat(emittedRaw);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null) =>
        JsonSerializer.SerializeToElement(SessionDbKey);
}
