using System.Text.Json;
using Microsoft.Extensions.Options;
using Web.Common.DTOs.AI;
using Web.Common.DTOs.Health;
using WebApi.Configuration.Options;
using WebApi.Hubs;
using WebApi.Models;
using WebApi.Services.AI.Workflows;
using WebApi.Services.Chat.Conversations;
using WebApi.Services.VectorStore;

namespace WebApi.Services.AI.Scenarios;

/// <summary>
/// Concrete implementation of the health chat AI scenario using Agent Framework workflows.
/// Handles symptom tracking and assessment creation with deterministic workflows.
/// </summary>
public class HealthChatScenario(
    AssessmentWorkflow assessmentWorkflow,
    SymptomTrackingWorkflow symptomTrackingWorkflow,
    VectorStoreService vectorStoreService,
    IOptions<VectorStoreSettings> vectorStoreSettings,
    ConversationContextService contextService,
    ILogger<HealthChatScenario> logger)
{
    private readonly VectorStoreSettings _vectorStoreSettings = vectorStoreSettings.Value;

    /// <summary>
    /// Executes the health chat scenario using workflows.
    /// </summary>
    public async Task<HealthChatScenarioResponse> ExecuteAsync(
        HealthChatScenarioRequest input,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsyncInternal(input, cancellationToken, null);
    }

    /// <summary>
    /// Internal execution method that accepts ClientConnection for SignalR updates.
    /// </summary>
    public async Task<HealthChatScenarioResponse> ExecuteAsyncInternal(
        HealthChatScenarioRequest input,
        CancellationToken cancellationToken,
        ClientConnection? clientConnection)
    {
        var statusUpdatesSent = new List<object>();
        var explicitChanges = new List<EntityChange>();

        try
        {
            // Hydrate conversation context
            logger.LogDebug("Hydrating context for UserId: {UserId}, ConversationId: {ConversationId}",
                input.UserId, input.ConversationId);
            var conversationContext = await contextService.HydrateContextAsync(
                input.UserId,
                input.ConversationId);
            logger.LogDebug("Context hydrated. ConversationId in context: {ConversationId}",
                conversationContext.ConversationId);

            // Set client connection for workflows and tools
            assessmentWorkflow.SetConnection(clientConnection);
            symptomTrackingWorkflow.SetConnection(clientConnection);

            // Determine which workflow to use based on user intent
            var userMessageLower = input.Message.ToLowerInvariant();
            var assessmentKeywords = new[] { "assessment", "assess", "diagnosis", "evaluate", "evaluation", "generate assessment", "create assessment" };
            var assessmentRequested = assessmentKeywords.Any(keyword => userMessageLower.Contains(keyword));

            WorkflowResult workflowResult;
            if (assessmentRequested)
            {
                logger.LogInformation("User requested assessment, using AssessmentWorkflow");
                workflowResult = await assessmentWorkflow.ExecuteAsync(input.Message, conversationContext);
            }
            else
            {
                logger.LogInformation("User message is symptom-related, using SymptomTrackingWorkflow");
                workflowResult = await symptomTrackingWorkflow.ExecuteAsync(input.Message, conversationContext);
            }

            // Flush context changes
            await contextService.FlushContextAsync(conversationContext);

            // Extract explicit changes from workflow state
            if (workflowResult.State.ContainsKey("createdEpisodes"))
            {
                var episodes = (List<Episode>)workflowResult.State["createdEpisodes"];
                foreach (var episode in episodes)
                {
                    explicitChanges.Add(new EntityChange
                    {
                        Action = "created",
                        Id = episode.Id.ToString(),
                        Name = episode.Symptom?.Name ?? "Unknown symptom"
                    });
                }
            }

            if (workflowResult.AssessmentId.HasValue)
            {
                var assessmentId = workflowResult.AssessmentId.Value;
                var assessment = conversationContext.CurrentAssessment;
                explicitChanges.Add(new EntityChange
                {
                    Action = "created",
                    Id = assessmentId.ToString(),
                    Confidence = assessment?.Confidence
                });
            }

            // Get tracked status updates from client connection if available
            if (clientConnection != null)
            {
                statusUpdatesSent = clientConnection.GetTrackedStatusUpdates();
            }

            // Serialize response as JSON for backward compatibility with orchestrator
            var responseMessage = workflowResult.Response;
            var response = new HealthChatScenarioResponse
            {
                Message = responseMessage,
                StatusUpdatesSent = statusUpdatesSent,
                ExplicitChanges = explicitChanges
            };

            logger.LogInformation("[HEALTH_CHAT_SCENARIO] Returning response with {Count} status updates tracked", statusUpdatesSent.Count);

            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing health chat scenario for user {UserId}", input.UserId);
            throw;
        }
    }

    /// <summary>
    /// Gets the final structured response if available (for backward compatibility).
    /// </summary>
    public HealthAssistantResponse? GetFinalResponse()
    {
        // Workflows return structured responses directly, so this is mainly for compatibility
        return null;
    }
}
