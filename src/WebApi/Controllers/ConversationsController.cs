using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Web.Common.DTOs;
using WebApi.Controllers.Utils;
using WebApi.Data;
using WebApi.Services.Chat;
using WebApi.Services.Chat.Formatters;
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
    HttpResponseWriter responseWriter,
    AppDbContext dbContext,
    MessageFormatter messageFormatter,
    AiState aiState)
    : BaseController
{
    /// <summary>
    /// List all conversations (sessions) for the user.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<object>>> ListConversations()
    {
        var userId = CurrentUser.UserId;
        var userSessionIds = await dbContext.Sessions
            .Where(s => s.UserId == userId)
            .Select(s => s.Id)
            .ToListAsync();

        var conversations = await dbContext.ChatMessages
            .Where(m => userSessionIds.Contains(m.SessionId!) && m.MessageText != null)
            .GroupBy(m => m.SessionId!)
            .Select(g => new
            {
                id = g.Key,
                title = g.OrderBy(m => m.Timestamp).First().MessageText,
                last_message_preview = g.OrderByDescending(m => m.Timestamp).First().MessageText,
                updated_at = g.Max(m => m.Timestamp)
            })
            .OrderByDescending(s => s.updated_at)
            .ToListAsync();

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
        var session = await dbContext.Sessions.FindAsync(conversationId);
        if (session == null) return NotFound(new ErrorResponse { Message = "Conversation not found" });
        if (session.UserId != null && session.UserId != CurrentUser.UserId) return Forbid();

        try
        {
            var entities = await dbContext.ChatMessages
                .Where(e => e.SessionId == conversationId)
                .OrderBy(e => e.Timestamp)
                .ToListAsync();

            if (entities.Count == 0)
                throw new ArgumentException("Conversation not found");

            var messages = entities
                .Where(e => e.SerializedMessage != null)
                .Select(e => (message: JsonSerializer.Deserialize<ChatMessage>(e.SerializedMessage!)!, entity: e))
                // filter goes here
                .Select(x => new
                {
                    role = x.message.Role.ToString().ToLower(),
                    content = x.message.Role == ChatRole.User
                        ? string.Join(" ", x.message.Contents.OfType<TextContent>().Select(tc => tc.Text))
                        : FormatAsNdjson(x.message, x.entity.EmittedTags)
                }).ToList();

            var conversation = new
            {
                id = conversationId,
                title = (string?)null,
                messages = messages
            };

            return Ok(conversation);
        }
        catch (ArgumentException)
        {
            return NotFound(new ErrorResponse { Message = "Conversation not found" });
        }
    }

    [HttpPost("messages")]
    public async Task SendMessage([FromBody] ChatMessageRequest request, CancellationToken cancellationToken)
    {
        aiState.UserId = CurrentUser.UserId;
        // Best-effort: set ConversationId from existing session key so assessment plugin can reference it
        if (!string.IsNullOrEmpty(request.ConversationId) && Guid.TryParse(request.ConversationId, out var convGuid))
            aiState.ConversationId = convGuid;

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

    private string FormatAsNdjson(ChatMessage message, string? emittedTags = null)
    {
        var sb = new StringBuilder();

        // Prepend out-of-band NDJSON lines verbatim (SymptomCreated, AssessmentCreated, ToolCall events).
        if (!string.IsNullOrEmpty(emittedTags))
            sb.Append(emittedTags);

        foreach (var chunk in messageFormatter.FormatMessage(message))
        {
            if (MessageFormatter.ShouldSkip(chunk.Category))
                continue;

            var type = chunk.Category switch
            {
                ContentCategory.Text => "text",
                ContentCategory.Reasoning => "reasoning",
                ContentCategory.Usage => "usage",
                _ => "unknown"
            };

            var escaped = JsonSerializer.Serialize(chunk.Text);
            sb.Append($"{{\"type\":\"{type}\",\"content\":{escaped}}}\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Delete a conversation.
    /// </summary>
    [HttpDelete("{conversationId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteConversation(string conversationId)
    {
        var session = await dbContext.Sessions.FindAsync(conversationId);
        if (session == null) return NotFound();
        if (session.UserId != null && session.UserId != CurrentUser.UserId) return Forbid();

        var messages = await dbContext.ChatMessages
            .Where(m => m.SessionId == conversationId)
            .ToListAsync();

        dbContext.ChatMessages.RemoveRange(messages);
        await dbContext.SaveChangesAsync();

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
