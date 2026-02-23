using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using WebApi.Data;
using WebApi.Models;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace WebApi.Services.AI.ChatHistory;

/// <summary>
/// ChatHistoryProvider implementation that stores chat history in the database Messages table.
/// Maps ConversationId (Guid) to session state for conversation continuity.
/// </summary>
public class DatabaseChatHistoryProvider : ChatHistoryProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Guid _conversationId;
    private readonly ILogger<DatabaseChatHistoryProvider>? _logger;

    /// <summary>
    /// Constructor for new sessions with a ConversationId.
    /// </summary>
    public DatabaseChatHistoryProvider(
        IServiceProvider serviceProvider,
        Guid conversationId,
        ILogger<DatabaseChatHistoryProvider>? logger = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _conversationId = conversationId;
        _logger = logger;
    }

    /// <summary>
    /// Constructor for deserialized sessions - reads ConversationId from serialized state.
    /// </summary>
    public DatabaseChatHistoryProvider(
        IServiceProvider serviceProvider,
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        ILogger<DatabaseChatHistoryProvider>? logger = null)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger;

        // Deserialize ConversationId from session state
        if (serializedState.ValueKind == JsonValueKind.String)
        {
            var conversationIdString = serializedState.Deserialize<string>(jsonSerializerOptions);
            if (Guid.TryParse(conversationIdString, out var conversationId))
            {
                _conversationId = conversationId;
            }
            else
            {
                throw new ArgumentException($"Invalid ConversationId in serialized state: {conversationIdString}", nameof(serializedState));
            }
        }
        else
        {
            throw new ArgumentException($"Expected string value for ConversationId, got {serializedState.ValueKind}", nameof(serializedState));
        }
    }

    /// <summary>
    /// Gets a scoped AppDbContext for database operations.
    /// Assumes we're in a request scope (which should be true for HTTP requests).
    /// </summary>
    private AppDbContext GetContext()
    {
        // Get the scoped context from the current request scope
        // This should be available when InvokingAsync/InvokedAsync are called during HTTP requests
        return _serviceProvider.GetRequiredService<AppDbContext>();
    }

    /// <summary>
    /// Gets the ConversationId associated with this provider instance.
    /// </summary>
    public Guid ConversationId => _conversationId;

    /// <summary>
    /// Updates the StatusInformationJson for the most recent assistant message in the conversation.
    /// This is called after agent execution when status information is computed from entity changes.
    /// </summary>
    public async Task UpdateLastAssistantMessageStatusAsync(
        string? statusInformationJson,
        CancellationToken cancellationToken = default)
    {
        var dbContext = GetContext();
        try
        {
            // Find the most recent assistant message for this conversation
            var lastAssistantMessage = await dbContext.Messages
                .Where(m => m.ConversationId == _conversationId && m.Role == "assistant")
                .OrderByDescending(m => m.CreatedAt)
                .ThenByDescending(m => m.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastAssistantMessage != null)
            {
                lastAssistantMessage.StatusInformationJson = statusInformationJson;
                await dbContext.SaveChangesAsync(cancellationToken);

                _logger?.LogDebug(
                    "Updated StatusInformationJson for message {MessageId} in conversation {ConversationId}",
                    lastAssistantMessage.Id,
                    _conversationId);
            }
            else
            {
                _logger?.LogWarning(
                    "No assistant message found to update StatusInformationJson for conversation {ConversationId}",
                    _conversationId);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error updating StatusInformationJson for conversation {ConversationId}", _conversationId);
            // Don't throw - this is a non-critical update
        }
    }

    /// <summary>
    /// Loads chat history from the database before agent invocation.
    /// Called automatically by the Agent Framework.
    /// </summary>
    public override async ValueTask<IEnumerable<ChatMessage>> InvokingAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var dbContext = GetContext();
        try
        {
            // Load messages from database for this conversation, ordered chronologically
            var messages = await dbContext.Messages
                .Where(m => m.ConversationId == _conversationId)
                .OrderBy(m => m.CreatedAt)
                .ThenBy(m => m.Id) // Secondary sort for deterministic ordering
                .ToListAsync(cancellationToken);

            _logger?.LogDebug(
                "Loaded {Count} messages from database for conversation {ConversationId}",
                messages.Count,
                _conversationId);

            // Convert Message entities to ChatMessage objects
            var chatMessages = messages.Select(ConvertToChatMessage).ToList();

            return chatMessages;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading chat history for conversation {ConversationId}", _conversationId);
            // Return empty list on error to allow agent to continue
            return Array.Empty<ChatMessage>();
        }
    }

    /// <summary>
    /// Saves new chat messages to the database after agent invocation.
    /// Called automatically by the Agent Framework.
    /// </summary>
    public override async ValueTask InvokedAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        // Don't store messages if the request failed
        if (context.InvokeException is not null)
        {
            _logger?.LogWarning(
                "Skipping message save due to invocation exception for conversation {ConversationId}",
                _conversationId);
            return;
        }

        var dbContext = GetContext();
        try
        {
            // Combine all messages: request, AI context provider messages, and response messages
            var allNewMessages = context.RequestMessages
                .Concat(context.AIContextProviderMessages ?? [])
                .Concat(context.ResponseMessages ?? [])
                .ToList();

            if (!allNewMessages.Any())
            {
                _logger?.LogDebug("No new messages to save for conversation {ConversationId}", _conversationId);
                return;
            }

            // Check which messages already exist to avoid duplicates
            // We'll check by comparing content and timestamp to avoid saving duplicates
            var existingMessages = await dbContext.Messages
                .Where(m => m.ConversationId == _conversationId)
                .Select(m => new { m.Content, m.CreatedAt, m.Role })
                .ToListAsync(cancellationToken);

            var messagesToSave = new List<Message>();

            foreach (var chatMessage in allNewMessages)
            {
                // Skip if this message already exists (check by content and role)
                var messageText = chatMessage.Text ?? string.Empty;
                var messageRole = ConvertChatRoleToString(chatMessage.Role);

                // Only check for duplicates if we have existing messages
                if (existingMessages.Any(existing =>
                    existing.Content == messageText &&
                    existing.Role == messageRole &&
                    Math.Abs((existing.CreatedAt - DateTime.UtcNow).TotalSeconds) < 5)) // Within 5 seconds
                {
                    _logger?.LogDebug(
                        "Skipping duplicate message for conversation {ConversationId}: {Role} - {Content}",
                        _conversationId,
                        messageRole,
                        messageText.Length > 50 ? messageText.Substring(0, 50) + "..." : messageText);
                    continue;
                }

                var message = ConvertToMessage(chatMessage, _conversationId);
                messagesToSave.Add(message);
            }

            if (messagesToSave.Any())
            {
                dbContext.Messages.AddRange(messagesToSave);

                // Update conversation's UpdatedAt timestamp
                var conversation = await dbContext.Conversations
                    .FirstOrDefaultAsync(c => c.Id == _conversationId, cancellationToken);

                if (conversation != null)
                {
                    conversation.UpdatedAt = DateTime.UtcNow;
                }

                await dbContext.SaveChangesAsync(cancellationToken);

                _logger?.LogInformation(
                    "Saved {Count} new messages to database for conversation {ConversationId}",
                    messagesToSave.Count,
                    _conversationId);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error saving chat history for conversation {ConversationId}", _conversationId);
            // Don't throw - allow agent execution to complete even if save fails
        }
    }

    /// <summary>
    /// Serializes the session state (ConversationId) for persistence.
    /// </summary>
    public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        // Serialize ConversationId as a string for session persistence
        return JsonSerializer.SerializeToElement(_conversationId.ToString(), jsonSerializerOptions);
    }

    /// <summary>
    /// Converts a Message entity to a ChatMessage object.
    /// </summary>
    private static ChatMessage ConvertToChatMessage(Message message)
    {
        var role = ConvertStringToChatRole(message.Role);
        var chatMessage = new ChatMessage(role, message.Content)
        {
            MessageId = message.Id.ToString()
        };

        // Store StatusInformationJson in metadata if present (for assistant messages)
        if (!string.IsNullOrEmpty(message.StatusInformationJson) && message.Role == "assistant")
        {
            // Store in AdditionalProperties if available, or we'll handle it separately
            // Note: ChatMessage doesn't have a direct metadata field, so we'll need to handle StatusInformationJson
            // separately in the orchestrator when needed
        }

        return chatMessage;
    }

    /// <summary>
    /// Converts a ChatMessage object to a Message entity.
    /// </summary>
    private static Message ConvertToMessage(ChatMessage chatMessage, Guid conversationId)
    {
        return new Message
        {
            Id = Guid.TryParse(chatMessage.MessageId, out var messageId) ? messageId : Guid.NewGuid(),
            ConversationId = conversationId,
            Role = ConvertChatRoleToString(chatMessage.Role),
            Content = chatMessage.Text ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Converts a ChatRole enum to a string for database storage.
    /// </summary>
    private static string ConvertChatRoleToString(ChatRole role)
    {
        if (role == ChatRole.User)
            return "user";
        if (role == ChatRole.Assistant)
            return "assistant";
        if (role == ChatRole.System)
            return "system";
        return "user";
    }

    /// <summary>
    /// Converts a string role to a ChatRole enum.
    /// </summary>
    private static ChatRole ConvertStringToChatRole(string role)
    {
        return role.ToLowerInvariant() switch
        {
            "user" => ChatRole.User,
            "assistant" => ChatRole.Assistant,
            "system" => ChatRole.System,
            _ => ChatRole.User
        };
    }
}
