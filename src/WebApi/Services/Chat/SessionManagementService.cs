using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.OpenAI;
using Microsoft.Extensions.AI;
using WebApi.Services.AI.ChatHistory;
using ChatClientAgent = Microsoft.Agents.AI.ChatClientAgent;

namespace WebApi.Services.Chat;

/// <summary>
/// Service for managing agent sessions mapped to database conversations.
/// Handles session creation, retrieval, and persistence using ConversationId.
/// </summary>
public class SessionManagementService(
    IServiceProvider serviceProvider,
    ILogger<SessionManagementService> logger)
{
    /// <summary>
    /// Gets or creates an agent session for a conversation.
    /// </summary>
    /// <param name="agent">The agent to create a session for.</param>
    /// <param name="conversationId">The conversation ID to associate with the session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An agent session associated with the conversation.</returns>
    public async Task<AgentSession> GetOrCreateSessionAsync(
        AIAgent agent,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Create session by serializing the ConversationId as session state
            // The ChatHistoryProviderFactory will deserialize this and create the provider
            // We create a JsonElement with the ConversationId string
            var sessionState = JsonSerializer.SerializeToElement(conversationId.ToString());
            
            // Deserialize session from state - this will trigger ChatHistoryProviderFactory
            var session = await agent.DeserializeSessionAsync(sessionState, cancellationToken: cancellationToken);

            logger.LogDebug(
                "Created/retrieved session for conversation {ConversationId}",
                conversationId);

            return session;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating session for conversation {ConversationId}", conversationId);
            throw;
        }
    }

    /// <summary>
    /// Gets an existing session by ConversationId.
    /// If the session doesn't exist, creates a new one.
    /// </summary>
    public async Task<AgentSession> GetSessionByConversationIdAsync(
        AIAgent agent,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        return await GetOrCreateSessionAsync(agent, conversationId, cancellationToken);
    }

    /// <summary>
    /// Serializes a session for persistence.
    /// </summary>
    public JsonElement SerializeSession(AIAgent agent, AgentSession session)
    {
        // Get the provider from the session to serialize its state
        // The provider's Serialize() method returns the ConversationId
        var provider = session.GetService<DatabaseChatHistoryProvider>();
        if (provider != null)
        {
            return provider.Serialize();
        }
        
        // Fallback: serialize ConversationId directly if provider not available
        // This shouldn't happen in normal flow
        throw new InvalidOperationException("Could not serialize session - DatabaseChatHistoryProvider not found in session");
    }

    /// <summary>
    /// Deserializes a session from persisted state.
    /// </summary>
    public async Task<AgentSession> DeserializeSessionAsync(
        AIAgent agent,
        JsonElement serializedState,
        CancellationToken cancellationToken = default)
    {
        // Use the agent's DeserializeSessionAsync method
        // This will trigger ChatHistoryProviderFactory with the serialized state
        return await agent.DeserializeSessionAsync(serializedState, cancellationToken: cancellationToken);
    }
}
