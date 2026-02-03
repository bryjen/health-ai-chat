using Microsoft.AspNetCore.Mvc;
using Web.Common.DTOs;
using Web.Common.DTOs.Conversations;
using WebApi.Controllers.Utils;
using WebApi.Exceptions;
using WebApi.Services.Chat;

namespace WebApi.Controllers;

// disabled to avoid no xml docs on injected services as parameters
#pragma warning disable CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)

/// <summary>
/// ***Manages conversations and messages for authenticated users***.
/// Provides endpoints to retrieve, update, and delete health chat conversations with their associated messages.
/// </summary>
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class ConversationsController : BaseController
{
    /// <summary>
    /// Retrieve Conversations
    /// </summary>
    /// <response code="200">Conversations retrieved successfully.</response>
    /// <response code="401">User isn't authenticated. **Returns standardized `ErrorResponse` model**.</response>
    /// <remarks>
    /// **Requires the request to be authenticated (stores user info).**
    ///
    /// Retrieves all conversations belonging to the user.
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
    /// Retrieve Specific Conversation
    /// </summary>
    /// <param name="id">The unique identifier of the conversation.</param>
    /// <response code="200">Conversation retrieved successfully.</response>
    /// <response code="401">User isn't authenticated. **Returns standardized `ErrorResponse` model**.</response>
    /// <response code="404">Conversation isn't found or does not belong to the current user. **Returns standardized `ErrorResponse` model**.</response>
    /// <remarks>
    /// **Requires the request to be authenticated (stores user info).**
    ///
    /// Retrieves a specific conversation by its unique identifier with all associated messages, in chronological order.
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
    /// Update Conversation Title
    /// </summary>
    /// <param name="id">The unique identifier of the conversation.</param>
    /// <param name="request">The new title for the conversation.</param>
    /// <returns>The updated conversation with all messages.</returns>
    /// <response code="200">Conversation title updated successfully.</response>
    /// <response code="400">Invalid input data or title validation failed. **Returns standardized `ErrorResponse` model**.</response>
    /// <response code="401">User isn't authenticated. **Returns standardized `ErrorResponse` model**.</response>
    /// <response code="404">Conversation isn't found or does not belong to the current user. **Returns standardized `ErrorResponse` model**.</response>
    /// <remarks>
    /// **Requires the request to be authenticated (stores user info).**
    ///
    /// Updates the title of an existing conversation (that the user owns).
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
    /// Delete Conversation
    /// </summary>
    /// <param name="id">The unique identifier of the conversation.</param>
    /// <returns>No content on successful deletion.</returns>
    /// <response code="204">Conversation deleted successfully.</response>
    /// <response code="401">User isn't authenticated. **Returns standardized `ErrorResponse` model**.</response>
    /// <response code="404">Conversation isn't found or does not belong to the current user. **Returns standardized `ErrorResponse` model**.</response>
    /// <remarks>
    /// **Requires the request to be authenticated (stores user info).**
    ///
    /// Permanently deletes a conversation and all associated messages, assessments, and related data.
    /// This operation is irreversible and should be used with caution.
    /// Only conversations belonging to the authenticated user can be deleted.
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
