using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Web.Common.DTOs.Health;
using WebApi.Services.Chat;

namespace WebApi.Hubs;

/// <summary>
/// SignalR hub for real-time chat communication
/// </summary>
[Authorize]
public class ChatHub(HealthChatOrchestrator orchestrator, ILogger<ChatHub> logger) : Hub
{
    private SignalRClientConnection? _clientConnection;

    /// <summary>
    /// Receives and processes a message through the health chat orchestrator
    /// </summary>
    /// <param name="message">The message to process</param>
    /// <param name="conversationId">Optional conversation ID to continue an existing conversation</param>
    /// <returns>Health chat response with message, conversation ID, and entity changes</returns>
    public async Task<HealthChatResponse> ProcessMessage(string message, Guid? conversationId = null)
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new HubException("User ID not found in token");
        }

        try
        {
            // Create a new connection instance for this request (tracks status updates)
            _clientConnection = new SignalRClientConnection(Clients.Caller, logger);

            var (response, _) = await orchestrator.ProcessHealthMessageAsync(
                userId, message, conversationId, _clientConnection);
            return response;
        }
        catch (Exception ex)
        {
            throw new HubException($"Error processing message: {ex.Message}");
        }
    }

    /// <summary>
    /// SignalR-specific implementation of ClientConnection
    /// </summary>
    private class SignalRClientConnection(
        IClientProxy clientProxy,
        ILogger logger) : ClientConnection
    {
        protected override void SendStatusUpdateCore(object statusData)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var json = JsonSerializer.Serialize(statusData);
                    await clientProxy.SendAsync("StatusUpdate", json);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send status update");
                }
            });
        }
    }
}
