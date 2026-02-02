using Microsoft.AspNetCore.Mvc;
using Web.Common.DTOs;
using Web.Common.DTOs.Health;
using Web.Common.DTOs.Conversations;
using WebApi.Controllers.Utils;
using WebApi.Exceptions;
using WebApi.Services.Chat;

namespace WebApi.Controllers;

/// <summary>
/// Handles healthcare-specific chat messages with AI-powered symptom tracking and appointment booking.
/// Processes user health concerns, tracks symptoms over time, and provides medical assessments with recommended actions.
/// </summary>
[Route("api/v1/health/chat")]
[Produces("application/json")]
public class HealthChatController : BaseController
{
    /// <summary>
    /// Sends a healthcare chat message to the AI health assistant for analysis and guidance.
    /// </summary>
    /// <param name="request">Message content and optional conversation ID for context.</param>
    /// <returns>Structured health assistant response containing AI message, symptom changes, appointment data, and conversation ID.</returns>
    /// <response code="200">Message processed successfully and added to existing conversation.</response>
    /// <response code="201">New conversation created with the message.</response>
    /// <response code="400">Invalid input data or validation failed.</response>
    /// <response code="401">User not authenticated.</response>
    /// <response code="404">Conversation not found when ConversationId is provided.</response>
    /// <remarks>
    /// Sends a healthcare-related message to the AI health assistant for analysis and guidance.
    /// The system automatically creates a new conversation if no ConversationId is provided.
    /// The AI analyzes symptoms and medical concerns, tracks symptom changes over time, determines appointment urgency levels, and provides follow-up questions.
    /// All messages are stored in the database and indexed in the vector store for semantic search across conversations.
    /// Symptom changes are tracked with actions: added, removed, or updated.
    /// Appointment urgency levels include Emergency, High, Medium, Low, and None.
    /// Use the conversationId from the response for subsequent messages in the same conversation.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(HealthChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HealthChatResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HealthChatResponse>> SendHealthMessage(
        [FromBody] HealthChatRequest request,
        [FromServices] HealthChatOrchestrator orchestrator,
        [FromServices] ILogger<HealthChatController> logger)
    {
        try
        {
            var userId = GetUserId();
            
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return this.BadRequestError("Message is required");
            }

            logger.LogDebug("Received health chat message from user {UserId}. ConversationId: {ConversationId}, Message length: {MessageLength}", 
                userId, request.ConversationId, request.Message.Length);

            // Orchestrate the entire health chat flow
            var (response, isNewConversation) = await orchestrator.ProcessHealthMessageAsync(
                userId,
                request.Message!,
                request.ConversationId);
            
            logger.LogDebug("Processed health chat message. ConversationId: {ConversationId}, IsNewConversation: {IsNewConversation}", 
                response.ConversationId, isNewConversation);

            // Return 201 if new conversation was created, 200 if existing conversation was updated
            if (isNewConversation)
            {
                return CreatedAtAction(
                    nameof(ConversationsController.GetConversationById),
                    "Conversations",
                    new { id = response.ConversationId },
                    response);
            }

            return Ok(response);
        }
        catch (NotFoundException ex)
        {
            return this.NotFoundError(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return this.UnauthorizedError(ex.Message);
        }
        catch (ValidationException ex)
        {
            return this.BadRequestError(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing health chat message");
            return this.BadRequestError("An error occurred while processing your message.");
        }
    }
}
