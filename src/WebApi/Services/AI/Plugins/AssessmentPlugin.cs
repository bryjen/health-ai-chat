using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using WebApi.Models;
using WebApi.Repositories;
using WebApi.Services.Chat;

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
    ConversationContextService _,
    ILogger<AssessmentPlugin> logger)
{
    private ConversationContext? _context;
    private Guid _userId;
    private Guid? _conversationId;
    private IStatusUpdateService? _statusUpdateService;

    /// <summary>
    /// Sets the current conversation context, user ID, conversation ID, and status update service for this plugin instance.
    /// Called before kernel function execution.
    /// </summary>
    public void SetContext(ConversationContext context, Guid userId, Guid conversationId, IStatusUpdateService? statusUpdateService = null)
    {
        _context = context;
        _userId = userId;
        _conversationId = conversationId;
        _statusUpdateService = statusUpdateService;
    }

    [KernelFunction]
    [Description("CreateAssessment: CALL THIS FUNCTION to create a medical assessment. REQUIRED when user asks for assessment or you have enough info for diagnosis. Parameters: hypothesis (your diagnosis), confidence (0.0-1.0, use 0.7 if unsure), differentials (optional alternatives), reasoning (optional), recommendedAction (see-gp/urgent-care/emergency/self-care). Episode weights are automatically assigned from active symptoms.")]
    public async Task<string> CreateAssessmentAsync(
        [Description("Your primary diagnosis or hypothesis")] string hypothesis,
        [Description("Confidence level 0.0 to 1.0 (use 0.7 if unsure)")] decimal confidence,
        [Description("Alternative diagnoses, comma-separated")] string? differentials = null,
        [Description("Your reasoning")] string reasoning = "",
        [Description("Recommended action: see-gp, urgent-care, emergency, or self-care")] string recommendedAction = "see-gp",
        [Description("Negative finding IDs, comma-separated")] string? negativeFindingIds = null)
    {
        if (_context == null || !_conversationId.HasValue)
        {
            throw new InvalidOperationException("Context not set. Call SetContext before using plugin functions.");
        }

        // Send "Generating assessment..." status
        if (_statusUpdateService != null)
        {
            await _statusUpdateService.SendGeneratingAssessmentAsync();
        }

        try
        {
            var differentialsList = differentials != null
                ? differentials.Split(',').Select(d => d.Trim()).ToList()
                : null;

            var negativeFindingIdsList = negativeFindingIds != null
                ? negativeFindingIds.Split(',').Select(id => int.TryParse(id.Trim(), out var nid) ? nid : (int?)null).Where(id => id.HasValue).Select(id => id!.Value).ToList()
                : _context.NegativeFindings.Select(nf => nf.Id).ToList();

            // Automatically assign episode weights from active episodes
            Dictionary<int, decimal> episodeWeightsDict = new();
            var activeEpisodes = _context.ActiveEpisodes.ToList();
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
                UserId = _userId,
                ConversationId = _conversationId.Value,
                Hypothesis = hypothesis,
                Confidence = Math.Clamp(confidence, 0, 1),
                Differentials = differentialsList,
                Reasoning = reasoning,
                RecommendedAction = recommendedAction,
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
            _context.CurrentAssessment = created;
            _context.Phase = ConversationPhase.Assessing;

            // Send status updates
            if (_statusUpdateService != null)
            {
                await _statusUpdateService.SendAssessmentCreatedAsync(created.Id, created.Hypothesis, created.Confidence);
                await Task.Delay(500); // Small delay for visibility
                await _statusUpdateService.SendAnalyzingAssessmentAsync();
                await Task.Delay(800); // Additional delay before final response
            }

            logger.LogInformation("Created assessment {AssessmentId} for conversation {ConversationId}", created.Id, _conversationId.Value);
            return $"Created assessment {created.Id}: {hypothesis} (confidence: {confidence:P0}). Recommended action: {recommendedAction}.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating assessment. Exception type: {ExceptionType}, Message: {Message}, StackTrace: {StackTrace}", 
                ex.GetType().Name, ex.Message, ex.StackTrace);
            // Return a more helpful error message
            return $"Error creating assessment: {ex.GetType().Name} - {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("UpdateAssessment: YOU MUST CALL THIS FUNCTION to update an existing assessment when you need to refine or change a previously created assessment. Use this when you have new information that changes the diagnosis, confidence level, or recommendations. Parameters: assessmentId (required - the ID of the assessment to update), then any fields you want to update (hypothesis, confidence, differentials, reasoning, recommendedAction, episodeWeights, negativeFindingIds).")]
    public async Task<string> UpdateAssessmentAsync(
        [Description("The ID of the assessment to update")] int assessmentId,
        [Description("Updated hypothesis or diagnosis")] string? hypothesis = null,
        [Description("Updated confidence level from 0.0 to 1.0")] decimal? confidence = null,
        [Description("Updated comma-separated list of alternative diagnoses")] string? differentials = null,
        [Description("Updated reasoning")] string? reasoning = null,
        [Description("Updated recommended action")] string? recommendedAction = null,
        [Description("Updated list of episode weights. Each weight maps an episode ID to a weight value (0.0 to 1.0). Example: [{\"episodeId\": 1, \"weight\": 0.8}, {\"episodeId\": 2, \"weight\": 0.6}]")] List<EpisodeWeight>? episodeWeights = null,
        [Description("Updated comma-separated list of negative finding IDs")] string? negativeFindingIds = null)
    {
        if (_context == null)
        {
            throw new InvalidOperationException("Context not set.");
        }

        try
        {
            List<string>? differentialsList = null;
            if (differentials != null)
            {
                differentialsList = differentials.Split(',').Select(d => d.Trim()).ToList();
            }

            List<int>? negativeFindingIdsList = null;
            if (negativeFindingIds != null)
            {
                negativeFindingIdsList = negativeFindingIds.Split(',').Select(id => int.TryParse(id.Trim(), out var nid) ? nid : (int?)null).Where(id => id.HasValue).Select(id => id!.Value).ToList();
            }

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
                differentialsList,
                reasoning,
                recommendedAction,
                episodeWeightsDict,
                negativeFindingIdsList);

            if (updated == null)
            {
                return $"Assessment {assessmentId} not found.";
            }

            // Update context
            if (_context.CurrentAssessment != null && _context.CurrentAssessment.Id == assessmentId)
            {
                _context.CurrentAssessment = updated;
            }

            // Send status updates
            if (_statusUpdateService != null)
            {
                await _statusUpdateService.SendAssessmentCreatedAsync(updated.Id, updated.Hypothesis, updated.Confidence);
                await Task.Delay(500);
                await _statusUpdateService.SendAnalyzingAssessmentAsync();
                await Task.Delay(800);
            }

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
