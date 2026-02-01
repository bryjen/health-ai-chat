using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Web.Common.DTOs.Health;
using WebApi.Services.Chat;

namespace WebApi.Hubs;

/// <summary>
/// SignalR hub for real-time chat communication
/// </summary>
[Authorize]
public class ChatHub : Hub
{
    private readonly HealthChatOrchestrator _orchestrator;

    public ChatHub(HealthChatOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    /// <summary>
    /// Sends a message and processes it through the health chat orchestrator
    /// </summary>
    /// <param name="message">The message to send</param>
    /// <param name="conversationId">Optional conversation ID to continue an existing conversation</param>
    /// <returns>Health chat response with message, conversation ID, and entity changes</returns>
    public async Task<HealthChatResponse> SendMessage(string message, Guid? conversationId = null)
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new HubException("User ID not found in token");
        }

        try
        {
            var (response, _) = await _orchestrator.ProcessHealthMessageAsync(userId, message, conversationId);
            return response;
        }
        catch (Exception ex)
        {
            throw new HubException($"Error processing message: {ex.Message}");
        }
    }
}
