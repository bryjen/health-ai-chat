using Microsoft.EntityFrameworkCore;
using Web.Common.DTOs.Health;
using WebApi.Data;
using Web.Common.DTOs.AI;
using WebApi.Exceptions;
using WebApi.Hubs;
using WebApi.Models;
using WebApi.Services.AI.Scenarios;
using WebApi.Services.VectorStore;

namespace WebApi.Services.Chat;

/// <summary>
/// Orchestrates the health chat flow: conversation management, AI processing, message persistence, and embedding storage.
/// </summary>
public class HealthChatOrchestrator(
    HealthChatScenario scenario,
    ResponseRouterService responseRouter,
    VectorStoreService vectorStoreService,
    AppDbContext context,
    StatusInformationSerializer statusSerializer,
    HealthResponseParser responseParser,
    EntityChangeTracker changeTracker,
    ILogger<HealthChatOrchestrator> logger)
{
    public async Task<(HealthChatResponse Response, bool IsNewConversation)> ProcessHealthMessageAsync(
        Guid userId,
        string message,
        Guid? conversationId = null,
        ClientConnection? clientConnection = null)
    {
        var (conversation, isNewConversation) = await GetOrCreateConversationAsync(userId, message, conversationId);

        var (healthResponse, explicitChanges) = await ProcessMessageAsync(
            userId,
            message,
            conversation.Id,
            clientConnection);

        var routedResponse = responseRouter.RouteResponse(healthResponse, userId);

        // Use explicit changes from tool execution instead of post-hoc database queries
        List<EntityChange> symptomChanges;
        List<EntityChange> appointmentChanges;
        List<EntityChange> assessmentChanges;

        if (explicitChanges.Any())
        {
            // Use explicit changes - more reliable than database queries
            symptomChanges = explicitChanges
                .Where(c => (c.Action == "created" || c.Action == "updated" || c.Action == "resolved") 
                            && !string.IsNullOrEmpty(c.Name) && c.Name != "Unknown symptom")
                .ToList();
            assessmentChanges = explicitChanges
                .Where(c => c.Action == "created" && !string.IsNullOrEmpty(c.Id) && int.TryParse(c.Id, out _) && c.Confidence.HasValue)
                .ToList();
            appointmentChanges = new List<EntityChange>(); // Appointments not yet tracked explicitly
        }
        else
        {
            // Fallback to database queries for backward compatibility
            var (episodesBeforeDict, appointmentsBefore, assessmentsBefore) = 
                await changeTracker.GetBeforeStateAsync(userId, conversation.Id);

            symptomChanges = await changeTracker.TrackEpisodeChangesAsync(userId, routedResponse.SymptomChanges, episodesBeforeDict);
            appointmentChanges = await changeTracker.TrackAppointmentChangesAsync(userId, appointmentsBefore);
            assessmentChanges = await changeTracker.TrackAssessmentChangesAsync(userId, conversation.Id, assessmentsBefore);
        }

        // Merge real-time status updates with EntityChanges-based statuses
        logger.LogInformation("[ORCHESTRATOR] Serializing status information. StatusUpdatesSent count: {Count}", 
            routedResponse.StatusUpdatesSent?.Count ?? 0);
        var statusInformationJson = statusSerializer.Serialize(
            symptomChanges,
            appointmentChanges,
            assessmentChanges,
            routedResponse.StatusUpdatesSent);
        logger.LogInformation("[ORCHESTRATOR] StatusInformationJson length: {Length}", statusInformationJson?.Length ?? 0);

        var (userMessage, assistantMessage) = await SaveMessagesAsync(
            conversation.Id,
            message,
            routedResponse.Message,
            statusInformationJson);

        await vectorStoreService.StoreMessageAsync(userId, userMessage.Id, message);
        await vectorStoreService.StoreMessageAsync(userId, assistantMessage.Id, routedResponse.Message);

        conversation.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        var response = new HealthChatResponse
        {
            Message = routedResponse.Message,
            ConversationId = conversation.Id,
            SymptomChanges = symptomChanges,
            AppointmentChanges = appointmentChanges,
            AssessmentChanges = assessmentChanges
        };

        return (response, isNewConversation);
    }

    private async Task<(Conversation Conversation, bool IsNewConversation)> GetOrCreateConversationAsync(
        Guid userId,
        string message,
        Guid? conversationId)
    {
        if (conversationId.HasValue)
        {
            // Continue existing conversation
            logger.LogDebug("Looking up existing conversation {ConversationId} for user {UserId}", conversationId.Value,
                userId);
            var conversation = await context.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId.Value && c.UserId == userId);

            if (conversation == null)
            {
                logger.LogWarning("Conversation {ConversationId} not found for user {UserId}", conversationId.Value,
                    userId);
                throw new NotFoundException("Conversation not found");
            }

            logger.LogDebug("Found existing conversation {ConversationId}, continuing conversation", conversation.Id);
            return (conversation, false);
        }
        else
        {
            // Create new conversation
            logger.LogDebug("No conversationId provided, creating new conversation for user {UserId}", userId);
            var title = message.Length > 50
                ? message.Substring(0, 50) + "..."
                : message;

            var conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                Title = title,
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.Conversations.Add(conversation);
            await context.SaveChangesAsync(); // Save to get the ID

            logger.LogDebug("Created new conversation {ConversationId} for user {UserId}", conversation.Id, userId);
            return (conversation, true);
        }
    }

    private async Task<(Message UserMessage, Message AssistantMessage)> SaveMessagesAsync(
        Guid conversationId,
        string userMessageContent,
        string assistantMessageContent,
        string? statusInformationJson = null)
    {
        var userMessage = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = "user",
            Content = userMessageContent,
            CreatedAt = DateTime.UtcNow
        };

        var assistantMessage = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = "assistant",
            Content = assistantMessageContent,
            StatusInformationJson = statusInformationJson,
            CreatedAt = DateTime.UtcNow
        };

        context.Messages.Add(userMessage);
        context.Messages.Add(assistantMessage);
        await context.SaveChangesAsync();

        return (userMessage, assistantMessage);
    }


    private async Task<(HealthAssistantResponse Response, List<EntityChange> ExplicitChanges)> ProcessMessageAsync(
        Guid userId,
        string userMessage,
        Guid? conversationId,
        ClientConnection? clientConnection = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new HealthChatScenarioRequest
            {
                Message = userMessage,
                ConversationId = conversationId,
                UserId = userId
            };

            HealthChatScenarioResponse response;
            List<object> statusUpdatesSent = new();
            List<EntityChange> explicitChanges = new();
            if (scenario is HealthChatScenario healthChatScenario)
            {
                response = await healthChatScenario.ExecuteAsyncInternal(request, cancellationToken,
                    clientConnection);
                statusUpdatesSent = response.StatusUpdatesSent ?? new List<object>();
                explicitChanges = response.ExplicitChanges ?? new List<EntityChange>();
            }
            else
            {
                response = await scenario.ExecuteAsync(request, cancellationToken);
            }

            // Parse JSON response
            var parsedResponse = responseParser.Parse(response.Message);

            // Store status updates for later persistence
            parsedResponse.StatusUpdatesSent = statusUpdatesSent;

            return (parsedResponse, explicitChanges);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing health chat message for user {UserId}", userId);
            throw;
        }
    }

}
