using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using WebApi.Data;
using WebApi.Models.EfCore.Chat;
using WebApi.Services.Chat.HistoryProviders;

namespace WebApi.Services.Chat;

/// <summary>
/// Pure business logic for session management - NO UI dependencies.
/// Returns data, caller handles display. Web-ready.
/// </summary>
public class SessionManager(
    IServiceScopeFactory serviceScopeFactory,
    AIAgent agent)
{
    /// <summary>
    /// Get all sessions ordered by most recent.
    /// Web: Return as JSON
    /// Console: Display in selection menu
    /// </summary>
    public async Task<List<SessionEntity>> GetSessionsAsync()
    {
        using var scope = serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await dbContext.Sessions
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Get chat history for a session (for display purposes).
    /// </summary>
    public async Task<List<ChatMessage>> GetSessionHistoryAsync(string sessionId)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var messages = await dbContext.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();

        return messages
            .Select(m => JsonSerializer.Deserialize<ChatMessage>(m.SerializedMessage!))
            .Where(m => m is not null)
            .Cast<ChatMessage>()
            .ToList();
    }

    /// <summary>
    /// Create a new session.
    /// </summary>
    public async Task<AgentSession> CreateNewSessionAsync()
    {
        return await agent.GetNewSessionAsync();
    }

    /// <summary>
    /// Load an existing session from database.
    /// Throws InvalidOperationException if session has corrupted tool message pairing.
    /// </summary>
    public async Task<AgentSession> LoadSessionAsync(string sessionId)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sessionEntity = await dbContext.Sessions.FindAsync(sessionId);
        if (sessionEntity == null)
        {
            throw new ArgumentException($"Session {sessionId} not found");
        }

        var stateJson = JsonSerializer.Deserialize<JsonElement>(sessionEntity.SerializedState);
        var session = await agent.DeserializeSessionAsync(stateJson);

        return session;
    }

    /// <summary>
    /// Persist session state to database.
    /// Called after each chat turn.
    /// </summary>
    public async Task PersistSessionAsync(AgentSession session)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        JsonElement serializedSession = session.Serialize();
        var sessionDbKey = session.GetService<DbChatHistoryProvider>()!.SessionDbKey!;
        var existingSession = await dbContext.Sessions.FindAsync(sessionDbKey);

        if (existingSession is not null)
        {
            existingSession.SerializedState = JsonSerializer.Serialize(serializedSession);
            existingSession.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            dbContext.Sessions.Add(new SessionEntity
            {
                Id = sessionDbKey,
                SerializedState = JsonSerializer.Serialize(serializedSession),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await dbContext.SaveChangesAsync();
    }
}

