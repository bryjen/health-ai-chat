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

        var records = await dbContext.ChatMessages
            .Where(x => x.SessionId == SessionDbKey)
            .OrderByDescending(x => x.Timestamp)
            .Take(50)
            .ToListAsync(cancellationToken);

        records.Reverse();
        var messages = records.ConvertAll(x => JsonSerializer.Deserialize<ChatMessage>(x.SerializedMessage!)!);

        return messages;
    }

    public override async ValueTask InvokedAsync(
        InvokedContext context, CancellationToken cancellationToken = default)
    {
        if (context.InvokeException is not null)
            return;

        SessionDbKey ??= Guid.NewGuid().ToString("N");

        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Skip tool intermediary messages — FunctionInvokingChatClient handles the tool loop
        // internally and never saves the tool_result side, so saving tool_use would corrupt history
        // (Anthropic requires tool_use → tool_result pairs). The final assistant text already
        // summarises what happened, so the conversation remains coherent without them.
        var persistableMessages = context.RequestMessages
            .Concat(context.AIContextProviderMessages ?? [])
            .Concat(context.ResponseMessages ?? [])
            .Where(m => !m.Contents.Any(c => c is FunctionApprovalRequestContent
                or FunctionApprovalResponseContent
                or FunctionCallContent
                or FunctionResultContent))
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
