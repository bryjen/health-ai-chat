using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using WebApi.Hubs;
using WebApi.Models;
using WebApi.Repositories;
using WebApi.Services.Chat.Conversations;

namespace WebApi.Services.AI.Plugins;

/// <summary>
/// Represents an episode weight mapping for assessment creation.
/// Semantic Kernel can serialize this as a structured parameter.
/// </summary>
public class EpisodeWeight
{
    [Description("The episode ID")]
    public int EpisodeId { get; set; }

    [Description("The weight for this episode (0.0 to 1.0)")]
    public decimal Weight { get; set; }
}

/// <summary>
/// Semantic Kernel plugin for assessment operations.
/// Provides kernel functions that the AI can call to create and update assessments.
/// </summary>
public class AssessmentPlugin(
    AssessmentRepository assessmentRepository,
    ConversationContextService contextService,
    ILogger<AssessmentPlugin> logger)
{
    private ClientConnection? _clientConnection;

    public void SetConnection(ClientConnection? clientConnection)
    {
        _clientConnection = clientConnection;
    }

    private ConversationContext GetContext()
    {
        try
        {
            var context = contextService.GetCurrentContext();
            logger.LogDebug("Retrieved context. UserId: {UserId}, ConversationId: {ConversationId}",
                context.UserId, context.ConversationId);
            return context;
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Failed to get current context - context not hydrated");
            throw;
        }
    }

    private ClientConnection? GetClientConnection()
    {
        return _clientConnection;
    }

    [KernelFunction]
    [Description("CreateAssessment: MANDATORY FUNCTION TO CALL when user requests an assessment, diagnosis, or evaluation. You MUST call this function immediately when user says 'create assessment', 'generate assessment', 'assessment please', or any similar request. DO NOT describe assessments in text - CALL THIS FUNCTION. Parameters: hypothesis (required - your diagnosis as string, e.g. 'viral infection'), confidence (required - 0.0-1.0 decimal, use 0.7 if unsure), differentials (optional - array of alternative diagnoses, can be empty []), reasoning (optional - explanation), recommendedAction (optional - 'see-gp'/'urgent-care'/'emergency'/'self-care', defaults to 'see-gp'). Episode weights are automatically assigned from active symptoms.")]
    public async Task<string> CreateAssessmentAsync(
        [Description("REQUIRED: Your primary diagnosis or hypothesis as a string (e.g. 'viral infection', 'influenza', 'migraine')")] string hypothesis,
        [Description("REQUIRED: Confidence level 0.0 to 1.0 decimal (use 0.7 if unsure, 0.8-0.9 if confident)")] decimal confidence,
        [Description("OPTIONAL: Alternative diagnoses as an array of strings. Can be empty array [] or null if none.")] List<string>? differentials = null,
        [Description("OPTIONAL: Your reasoning or explanation for the diagnosis")] string reasoning = "",
        [Description("OPTIONAL: Recommended action - 'see-gp', 'urgent-care', 'emergency', or 'self-care' (defaults to 'see-gp')")] string recommendedAction = "see-gp",
        [Description("OPTIONAL: Negative finding IDs as an array of integers. Can be empty array [] or null if none.")] List<int>? negativeFindingIds = null)
    {
        logger.LogInformation("[ASSESSMENT_PLUGIN] CreateAssessmentAsync CALLED with hypothesis='{Hypothesis}', confidence={Confidence}, recommendedAction='{RecommendedAction}'",
            hypothesis, confidence, recommendedAction);
        try
        {
            var context = GetContext();
            var clientConnection = GetClientConnection();

            if (!context.ConversationId.HasValue)
            {
                logger.LogError("Cannot create assessment: ConversationId is null. UserId: {UserId}", context.UserId);
                return "Error: Cannot create assessment because no conversation is active. Please start a conversation first.";
            }

            // Send "Generating assessment..." status (fire-and-forget)
            clientConnection?.SendGeneratingAssessment();

            // Normalize differentials - filter out empty strings and trim
            var normalizedDifferentials = (differentials ?? new List<string>())
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Select(d => d.Trim())
                .ToList();
            if (!normalizedDifferentials.Any())
            {
                normalizedDifferentials = null;
            }

            // Use provided negative finding IDs or get from context
            var negativeFindingIdsList = (negativeFindingIds != null && negativeFindingIds.Any())
                ? negativeFindingIds
                : context.NegativeFindings.Select(nf => nf.Id).ToList();

            // Automatically assign episode weights from active episodes
            Dictionary<int, decimal> episodeWeightsDict = new();
            var activeEpisodes = context.ActiveEpisodes.ToList();
            if (activeEpisodes.Count > 0)
            {
                var defaultWeight = 1.0m / activeEpisodes.Count;
                foreach (var episode in activeEpisodes)
                {
                    episodeWeightsDict[episode.Id] = defaultWeight;
                }
            }

            var assessment = new Assessment
            {
                UserId = context.UserId,
                ConversationId = context.ConversationId.Value,
                Hypothesis = hypothesis,
                Confidence = Math.Clamp(confidence, 0, 1),
                Differentials = normalizedDifferentials,
                Reasoning = reasoning ?? string.Empty,
                RecommendedAction = recommendedAction ?? "see-gp",
                NegativeFindingIds = negativeFindingIdsList
            };

            // Create AssessmentEpisodeLink records
            foreach (var kvp in episodeWeightsDict)
            {
                assessment.LinkedEpisodes.Add(new AssessmentEpisodeLink
                {
                    EpisodeId = kvp.Key,
                    Weight = kvp.Value,
                    Reasoning = null
                });
            }

            var created = await assessmentRepository.CreateAssessmentAsync(assessment);

            // Update context
            context.CurrentAssessment = created;
            context.Phase = ConversationPhase.Assessing;

            // Send status updates (fire-and-forget)
            clientConnection?.SendAssessmentCreated(created.Id, created.Hypothesis, created.Confidence);
            clientConnection?.SendAnalyzingAssessment();

            logger.LogInformation("Created assessment {AssessmentId} for conversation {ConversationId}", created.Id, context.ConversationId.Value);
            return $"Created assessment {created.Id}: {hypothesis} (confidence: {confidence:P0}). Recommended action: {recommendedAction}.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating assessment. Exception type: {ExceptionType}, Message: {Message}, StackTrace: {StackTrace}",
                ex.GetType().Name, ex.Message, ex.StackTrace);

            // Log the actual parameters that were passed for debugging
            logger.LogError("CreateAssessment parameters - Hypothesis: {Hypothesis}, Confidence: {Confidence}, Differentials: {Differentials}, Reasoning: {Reasoning}, RecommendedAction: {RecommendedAction}",
                hypothesis, confidence, differentials != null ? string.Join(", ", differentials) : "null", reasoning, recommendedAction);

            // Return a more helpful error message that doesn't confuse the AI
            return $"Failed to create assessment due to a technical error. Please try again or contact support if the issue persists.";
        }
    }

    [KernelFunction]
    [Description("UpdateAssessment: YOU MUST CALL THIS FUNCTION to update an existing assessment when you need to refine or change a previously created assessment. Use this when you have new information that changes the diagnosis, confidence level, or recommendations. Parameters: assessmentId (required - the ID of the assessment to update), then any fields you want to update (hypothesis, confidence, differentials, reasoning, recommendedAction, episodeWeights, negativeFindingIds).")]
    public async Task<string> UpdateAssessmentAsync(
        [Description("The ID of the assessment to update")] int assessmentId,
        [Description("Updated hypothesis or diagnosis")] string? hypothesis = null,
        [Description("Updated confidence level from 0.0 to 1.0")] decimal? confidence = null,
        [Description("Updated list of alternative diagnoses")] List<string>? differentials = null,
        [Description("Updated reasoning")] string? reasoning = null,
        [Description("Updated recommended action")] string? recommendedAction = null,
        [Description("Updated list of episode weights. Each weight maps an episode ID to a weight value (0.0 to 1.0). Example: [{\"episodeId\": 1, \"weight\": 0.8}, {\"episodeId\": 2, \"weight\": 0.6}]")] List<EpisodeWeight>? episodeWeights = null,
        [Description("Updated list of negative finding IDs")] List<int>? negativeFindingIds = null)
    {
        var context = GetContext();
        var clientConnection = GetClientConnection();

        try
        {
            // Convert episode weights list to dictionary if provided
            Dictionary<int, decimal>? episodeWeightsDict = null;
            if (episodeWeights != null && episodeWeights.Count > 0)
            {
                episodeWeightsDict = new Dictionary<int, decimal>();
                foreach (var weight in episodeWeights)
                {
                    episodeWeightsDict[weight.EpisodeId] = Math.Clamp(weight.Weight, 0, 1);
                }
            }

            var updated = await assessmentRepository.UpdateAssessmentAsync(
                assessmentId,
                hypothesis,
                confidence.HasValue ? Math.Clamp(confidence.Value, 0, 1) : null,
                differentials,
                reasoning,
                recommendedAction,
                episodeWeightsDict,
                negativeFindingIds);

            if (updated == null)
            {
                return $"Assessment {assessmentId} not found.";
            }

            // Update context
            if (context.CurrentAssessment != null && context.CurrentAssessment.Id == assessmentId)
            {
                context.CurrentAssessment = updated;
            }

            // Send status updates (fire-and-forget)
            clientConnection?.SendAssessmentCreated(updated.Id, updated.Hypothesis, updated.Confidence);
            clientConnection?.SendAnalyzingAssessment();

            logger.LogInformation("Updated assessment {AssessmentId}", assessmentId);
            return $"Updated assessment {assessmentId}.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating assessment {AssessmentId}", assessmentId);
            return $"Error updating assessment: {ex.Message}";
        }
    }
}
