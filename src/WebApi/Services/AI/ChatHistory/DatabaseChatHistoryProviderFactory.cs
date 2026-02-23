using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebApi.Data;

namespace WebApi.Services.AI.ChatHistory;

/// <summary>
/// Factory for creating DatabaseChatHistoryProvider instances.
/// Resolves scoped services (AppDbContext, ILogger) and creates providers scoped to ConversationId.
/// </summary>
public class DatabaseChatHistoryProviderFactory
{
    private readonly IServiceProvider _serviceProvider;

    public DatabaseChatHistoryProviderFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// Creates a DatabaseChatHistoryProvider from serialized session state.
    /// The serialized state should contain the ConversationId as a string.
    /// </summary>
    public ChatHistoryProvider CreateProvider(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null)
    {
        var logger = _serviceProvider.GetService<ILogger<DatabaseChatHistoryProvider>>();

        // Create provider with service provider - it will resolve AppDbContext when needed
        return new DatabaseChatHistoryProvider(_serviceProvider, serializedState, jsonSerializerOptions, logger);
    }

    /// <summary>
    /// Creates a DatabaseChatHistoryProvider for a specific ConversationId.
    /// Used when creating new sessions.
    /// </summary>
    public ChatHistoryProvider CreateProvider(Guid conversationId)
    {
        var logger = _serviceProvider.GetService<ILogger<DatabaseChatHistoryProvider>>();

        // Create provider with service provider - it will resolve AppDbContext when needed
        return new DatabaseChatHistoryProvider(_serviceProvider, conversationId, logger);
    }
}
