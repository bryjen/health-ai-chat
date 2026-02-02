using Microsoft.AspNetCore.Mvc;
using Web.Common.DTOs;
using Web.Common.DTOs.Health;
using Web.Common.DTOs.Conversations;
using WebApi.Controllers.Utils;
using WebApi.Exceptions;
using WebApi.Services.Chat;

namespace WebApi.Controllers;

/// <summary>
/// Handles healthcare-specific chat messages with symptom tracking and appointment booking
/// </summary>
[Route("api/v1/health/chat")]
[Produces("application/json")]
public class HealthChatController : BaseController
{
    /// <summary>
    /// Send a healthcare chat message. Processes the message with AI, tracks symptoms, and handles appointment booking. Creates a new conversation if ConversationId is not provided.
    /// </summary>
    /// <param name="request">Message content (required) and optional conversation ID for context</param>
    /// <returns>Structured health assistant response with appointment data, symptom changes, follow-up questions, and conversation ID</returns>
    /// <response code="200">Message processed successfully and added to existing conversation</response>
    /// <response code="201">New conversation created with the message</response>
    /// <response code="400">Invalid input data</response>
    /// <response code="401">User not authenticated</response>
    /// <response code="404">Conversation not found (when ConversationId is provided)</response>
    /// <remarks>
    /// Sends a healthcare-related message to the AI health assistant. The system will:
    /// - Create a new conversation automatically if no ConversationId is provided
    /// - Analyze symptoms and medical concerns
    /// - Track symptom changes over time
    /// - Determine if an appointment is needed and urgency level
    /// - Provide follow-up questions to gather more information
    /// - Store the conversation and messages in the database
    /// 
    /// **Authentication Required**: Include JWT token in Authorization header
    /// 
    /// **Creating a New Conversation:**
    /// ```
    /// POST /api/v1/health/chat
    /// Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
    /// Content-Type: application/json
    /// 
    /// {
    ///   "message": "I have a headache that's been getting worse over the past 3 days. It's particularly bad in the morning and I've been feeling nauseous."
    /// }
    /// ```
    /// 
    /// **Continuing an Existing Conversation:**
    /// ```
    /// POST /api/v1/health/chat
    /// Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
    /// Content-Type: application/json
    /// 
    /// {
    ///   "message": "The headaches are worse in the morning and get better throughout the day",
    ///   "conversationId": "550e8400-e29b-41d4-a716-446655440000"
    /// }
    /// ```
    /// 
    /// **Example Response (201 Created - New Conversation):**
    /// ```json
    /// {
    ///   "message": "I understand you're experiencing worsening headaches with nausea. This could indicate several possibilities. Let me ask a few questions to better understand your situation:\n\n1. On a scale of 1-10, how severe is the pain?\n2. Have you experienced any vision changes?\n3. Are you taking any medications?\n\nBased on your symptoms, I recommend scheduling an appointment. The combination of worsening headaches and nausea warrants medical attention.",
    ///   "conversationId": "550e8400-e29b-41d4-a716-446655440000",
    ///   "symptomChanges": [
    ///     {
    ///       "id": "770e8400-e29b-41d4-a716-446655440001",
    ///       "action": "added"
    ///     },
    ///     {
    ///       "id": "880e8400-e29b-41d4-a716-446655440002",
    ///       "action": "added"
    ///     }
    ///   ],
    ///   "appointmentChanges": []
    /// }
    /// ```
    /// 
    /// **Example Response (200 OK - Existing Conversation):**
    /// ```json
    /// {
    ///   "message": "That pattern suggests it might be related to...",
    ///   "conversationId": "550e8400-e29b-41d4-a716-446655440000",
    ///   "symptomChanges": [
    ///     {
    ///       "id": "770e8400-e29b-41d4-a716-446655440001",
    ///       "action": "updated"
    ///     }
    ///   ],
    ///   "appointmentChanges": []
    /// }
    /// ```
    /// 
    /// **Example Response with Ready-to-Book Appointment:**
    /// ```json
    /// {
    ///   "message": "Based on your symptoms, I recommend scheduling an appointment within the next 2-3 days. Your symptoms suggest a non-emergency condition that should be evaluated by a healthcare provider.",
    ///   "conversationId": "550e8400-e29b-41d4-a716-446655440000",
    ///   "symptomChanges": [
    ///     {
    ///       "id": "770e8400-e29b-41d4-a716-446655440001",
    ///       "action": "updated"
    ///     }
    ///   ],
    ///   "appointmentChanges": [
    ///     {
    ///       "id": "990e8400-e29b-41d4-a716-446655440003",
    ///       "action": "created"
    ///     }
    ///   ]
    /// }
    /// ```
    /// 
    /// **Example Response for Emergency Situation:**
    /// ```json
    /// {
    ///   "message": "Your symptoms require immediate medical attention. Please go to the nearest emergency room or call 911 immediately.",
    ///   "conversationId": "550e8400-e29b-41d4-a716-446655440000",
    ///   "symptomChanges": [
    ///     {
    ///       "id": "770e8400-e29b-41d4-a716-446655440001",
    ///       "action": "added"
    ///     },
    ///     {
    ///       "id": "880e8400-e29b-41d4-a716-446655440002",
    ///       "action": "added"
    ///     }
    ///   ],
    ///   "appointmentChanges": []
    /// }
    /// ```
    /// 
    /// **Example Error Response (400 Bad Request):**
    /// ```json
    /// {
    ///   "error": "Bad Request",
    ///   "message": "Message is required"
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
    /// 
    /// **Urgency Levels:**
    /// - `Emergency`: Requires immediate medical attention (call 911 or go to ER)
    /// - `High`: Should see a doctor within 24 hours
    /// - `Medium`: Should schedule an appointment within 2-3 days
    /// - `Low`: Can schedule a routine appointment
    /// - `None`: No appointment needed
    /// 
    /// **Entity Change Actions:**
    /// - `added`: New symptom identified
    /// - `removed`: Symptom no longer present
    /// - `updated`: Symptom information updated
    /// - `created`: New appointment created
    /// 
    /// **Notes:**
    /// - If no conversationId is provided, a new conversation is automatically created
    /// - Conversation title is auto-generated from the first message (first 50 characters)
    /// - All messages are saved to the database and stored in the vector store for semantic search
    /// - The AI analyzes the message context and previous conversation history if available
    /// - Symptom and appointment changes are tracked by entity ID for efficient frontend updates
    /// - The `conversationId` in the response should be used for subsequent messages in the same conversation
    /// - To get full conversation details, use the GET `/api/v1/conversations/{id}` endpoint
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
