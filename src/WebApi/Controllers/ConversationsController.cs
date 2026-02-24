using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Web.Common.DTOs;
using WebApi.Controllers.Utils;
using WebApi.Services.Chat;
using WebApi.Services.Chat.HistoryProviders;
using WebApi.Services.Chat.Response;

namespace WebApi.Controllers;

/// <summary>
/// Manages conversations and chat messages for authenticated users.
/// </summary>
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class ConversationsController(
    SessionManager sessionManager,
    ChatService chatService,
    HttpResponseWriter responseWriter)
    : BaseController
{
    /// <summary>
    /// List all conversations (sessions) for the user.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<object>>> ListConversations()
    {
        var sessions = await sessionManager.GetSessionsAsync();

        var conversations = sessions.Select(s => new
        {
            id = s.Id,
            createdAt = s.CreatedAt,
            updatedAt = s.UpdatedAt
        }).ToList();

        return Ok(conversations);
    }

    /// <summary>
    /// Get a specific conversation with its message history.
    /// </summary>
    [HttpGet("{conversationId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<object>> GetConversation(string conversationId)
    {
        try
        {
            var history = await sessionManager.GetSessionHistoryAsync(conversationId);

            var messages = history.Select(m => new
            {
                role = m.Role.ToString().ToLower(),
                content = string.Join(" ", m.Contents
                    .OfType<TextContent>()
                    .Select(tc => tc.Text))
            }).ToList();

            var conversation = new
            {
                id = conversationId,
                messages = messages
            };

            return Ok(conversation);
        }
        catch (ArgumentException)
        {
            return NotFound(new ErrorResponse { Message = "Conversation not found" });
        }
    }

    [AllowAnonymous]
    [HttpPost("messages")]
    public async Task SendMessage([FromBody] ChatMessageRequest request, CancellationToken cancellationToken)
    {
        Response.ContentType = "application/json";
        Response.Headers.Append("Connection", "keep-alive");
        Response.Headers.Append("Cache-Control", "no-cache");

        if (string.IsNullOrWhiteSpace(request.Message))
            return;

        AgentSession? session;
        try
        {
            session = !string.IsNullOrEmpty(request.ConversationId)
                ? await sessionManager.LoadSessionAsync(request.ConversationId)
                : await sessionManager.CreateNewSessionAsync();
        }
        catch (ArgumentException ex)
        {
            await Response.WriteAsync(JsonSerializer.Serialize(new { type = "error", message = ex.Message }) + "\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            return;
        }

        await responseWriter.ResetState();
        await foreach (var update in chatService.ProcessMessageStreamAsync(request.Message, session).WithCancellation(cancellationToken))
        {
            await responseWriter.WriteChunkAsync(update, cancellationToken);
        }
        await responseWriter.CompleteAsync(cancellationToken);

        var sessionId = session.GetService<DbChatHistoryProvider>()?.SessionDbKey;
        await Response.WriteAsync(JsonSerializer.Serialize(new { type = "complete", conversationId = sessionId }) + "\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    [AllowAnonymous]
    [HttpGet("stream-test")]
    public async Task StreamTest(CancellationToken cancellationToken)
    {
        Response.ContentType = "application/json";
        Response.Headers.Append("Cache-Control", "no-cache");

        for (int i = 0; i < 5; i++)
        {
            await Response.WriteAsync($"chunk {i}\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            await Task.Delay(500, cancellationToken);
        }
    }

    /// <summary>
    /// Delete a conversation.
    /// </summary>
    [HttpDelete("{conversationId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteConversation(string conversationId)
    {
        // TODO: Implement actual deletion in SessionManager
        // For now, this is a placeholder
        return NoContent();
    }
}

/// <summary>
/// Request body for sending a message.
/// </summary>
public class ChatMessageRequest
{
    public required string Message { get; set; }
    public string? ConversationId { get; set; }
}
