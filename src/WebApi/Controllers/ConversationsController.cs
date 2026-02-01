using Microsoft.AspNetCore.Mvc;
using Web.Common.DTOs;
using Web.Common.DTOs.Conversations;
using WebApi.Controllers.Utils;
using WebApi.Exceptions;
using WebApi.Services.Chat;

namespace WebApi.Controllers;

/// <summary>
/// Manages conversations and messages for authenticated users
/// </summary>
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class ConversationsController : BaseController
{
    /// <summary>
    /// Get all conversations for the authenticated user
    /// </summary>
    /// <returns>List of conversation summaries</returns>
    /// <response code="200">Returns the list of conversations</response>
    /// <response code="401">User not authenticated</response>
    /// <remarks>
    /// Retrieves all conversations belonging to the authenticated user. Returns a summary list with conversation ID, title, last message preview, and update timestamp.
    /// 
    /// **Authentication Required**: Include JWT token in Authorization header
    /// 
    /// **Example Request:**
    /// ```
    /// GET /api/v1/conversations
    /// Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
    /// ```
    /// 
    /// **Example Response (200 OK):**
    /// ```json
    /// [
    ///   {
    ///     "id": "550e8400-e29b-41d4-a716-446655440000",
    ///     "title": "Health Consultation",
    ///     "lastMessagePreview": "I've been experiencing headaches...",
    ///     "updatedAt": "2024-01-15T10:30:00Z"
    ///   },
    ///   {
    ///     "id": "660e8400-e29b-41d4-a716-446655440001",
    ///     "title": "Follow-up Questions",
    ///     "lastMessagePreview": "Thank you for the information...",
    ///     "updatedAt": "2024-01-14T15:45:00Z"
    ///   }
    /// ]
    /// ```
    /// 
    /// **Example Error Response (401 Unauthorized):**
    /// ```json
    /// {
    ///   "error": "Unauthorized",
    ///   "message": "Authentication required"
    /// }
    /// ```
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(List<ConversationSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<ConversationSummaryDto>>> GetAllConversations(
        [FromServices] ConversationService conversationService)
    {
        var userId = GetUserId();
        var conversations = await conversationService.GetAllConversationsAsync(userId);
        return Ok(conversations);
    }

    /// <summary>
    /// Get a specific conversation by ID with all messages
    /// </summary>
    /// <param name="id">The unique identifier of the conversation (GUID format)</param>
    /// <returns>The requested conversation with all messages in chronological order</returns>
    /// <response code="200">Returns the conversation with all messages</response>
    /// <response code="401">User not authenticated</response>
    /// <response code="404">Conversation not found or doesn't belong to user</response>
    /// <remarks>
    /// Retrieves a complete conversation including all messages. Only conversations belonging to the authenticated user can be accessed.
    /// 
    /// **Authentication Required**: Include JWT token in Authorization header
    /// 
    /// **Example Request:**
    /// ```
    /// GET /api/v1/conversations/550e8400-e29b-41d4-a716-446655440000
    /// Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
    /// ```
    /// 
    /// **Example Response (200 OK):**
    /// ```json
    /// {
    ///   "id": "550e8400-e29b-41d4-a716-446655440000",
    ///   "title": "Health Consultation",
    ///   "messages": [
    ///     {
    ///       "id": "770e8400-e29b-41d4-a716-446655440002",
    ///       "role": "user",
    ///       "content": "I've been experiencing headaches for the past week",
    ///       "createdAt": "2024-01-15T10:00:00Z"
    ///     },
    ///     {
    ///       "id": "880e8400-e29b-41d4-a716-446655440003",
    ///       "role": "assistant",
    ///       "content": "I understand you're experiencing headaches. Can you tell me more about the severity?",
    ///       "createdAt": "2024-01-15T10:00:15Z"
    ///     }
    ///   ],
    ///   "createdAt": "2024-01-15T10:00:00Z",
    ///   "updatedAt": "2024-01-15T10:30:00Z"
    /// }
    /// ```
    /// 
    /// **Example Error Response (404 Not Found):**
    /// ```json
    /// {
    ///   "error": "Not Found",
    ///   "message": "Conversation not found"
    /// }
    /// ```
    /// </remarks>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ConversationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConversationDto>> GetConversationById(
        Guid id,
        [FromServices] ConversationService conversationService)
    {
        var userId = GetUserId();
        var conversation = await conversationService.GetConversationByIdAsync(id, userId);

        if (conversation == null)
        {
            return this.NotFoundError("Conversation not found");
        }

        return Ok(conversation);
    }

    /// <summary>
    /// Update the title of a conversation
    /// </summary>
    /// <param name="id">The unique identifier of the conversation (GUID format)</param>
    /// <param name="request">The new title for the conversation (1-200 characters)</param>
    /// <returns>The updated conversation with all messages</returns>
    /// <response code="200">Returns the updated conversation</response>
    /// <response code="400">Invalid input data (title missing or exceeds 200 characters)</response>
    /// <response code="401">User not authenticated</response>
    /// <response code="404">Conversation not found or doesn't belong to user</response>
    /// <remarks>
    /// Updates the title of an existing conversation. The title must be between 1 and 200 characters.
    /// 
    /// **Authentication Required**: Include JWT token in Authorization header
    /// 
    /// **Example Request:**
    /// ```
    /// PUT /api/v1/conversations/550e8400-e29b-41d4-a716-446655440000
    /// Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
    /// Content-Type: application/json
    /// 
    /// {
    ///   "title": "Updated Health Consultation - Headache Discussion"
    /// }
    /// ```
    /// 
    /// **Example Response (200 OK):**
    /// ```json
    /// {
    ///   "id": "550e8400-e29b-41d4-a716-446655440000",
    ///   "title": "Updated Health Consultation - Headache Discussion",
    ///   "messages": [...],
    ///   "createdAt": "2024-01-15T10:00:00Z",
    ///   "updatedAt": "2024-01-15T11:00:00Z"
    /// }
    /// ```
    /// 
    /// **Example Error Response (400 Bad Request):**
    /// ```json
    /// {
    ///   "error": "Bad Request",
    ///   "message": "Title is required"
    /// }
    /// ```
    /// 
    /// **Example Error Response (404 Not Found):**
    /// ```json
    /// {
    ///   "error": "Not Found",
    ///   "message": "Conversation not found"
    /// }
    /// ```
    /// </remarks>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ConversationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConversationDto>> UpdateConversationTitle(
        Guid id,
        [FromBody] UpdateConversationTitleRequest request,
        [FromServices] ConversationService conversationService)
    {
        try
        {
            var userId = GetUserId();
            var conversation = await conversationService.UpdateConversationTitleAsync(id, request.Title, userId);
            return Ok(conversation);
        }
        catch (NotFoundException ex)
        {
            return this.NotFoundError(ex.Message);
        }
    }

    /// <summary>
    /// Delete a conversation
    /// </summary>
    /// <param name="id">The unique identifier of the conversation (GUID format)</param>
    /// <returns>No content on success</returns>
    /// <response code="204">Conversation deleted successfully</response>
    /// <response code="401">User not authenticated</response>
    /// <response code="404">Conversation not found or doesn't belong to user</response>
    /// <remarks>
    /// Permanently deletes a conversation and all associated messages. This action cannot be undone.
    /// Only conversations belonging to the authenticated user can be deleted.
    /// 
    /// **Authentication Required**: Include JWT token in Authorization header
    /// 
    /// **Example Request:**
    /// ```
    /// DELETE /api/v1/conversations/550e8400-e29b-41d4-a716-446655440000
    /// Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
    /// ```
    /// 
    /// **Example Response (204 No Content):**
    /// ```
    /// (No response body)
    /// ```
    /// 
    /// **Example Error Response (404 Not Found):**
    /// ```json
    /// {
    ///   "error": "Not Found",
    ///   "message": "Conversation not found"
    /// }
    /// ```
    /// 
    /// **Note**: This operation is irreversible. All messages in the conversation will be permanently deleted.
    /// </remarks>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteConversation(
        Guid id,
        [FromServices] ConversationService conversationService)
    {
        try
        {
            var userId = GetUserId();
            await conversationService.DeleteConversationAsync(id, userId);
            return NoContent();
        }
        catch (NotFoundException ex)
        {
            return this.NotFoundError(ex.Message);
        }
    }
}
