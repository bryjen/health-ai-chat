using System.ComponentModel;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using WebApi.Models;
using WebApi.Repositories;
using WebApi.Services.Chat;

namespace WebApi.Services.AI.Plugins;

/// <summary>
/// Semantic Kernel plugin for assessment operations.
/// Provides kernel functions that the AI can call to create and update assessments.
/// </summary>
public class AssessmentPlugin(
    AssessmentRepository assessmentRepository,
    ConversationContextService contextService,
    ILogger<AssessmentPlugin> logger)
{
    private ConversationContext? _context;
    private Guid _userId;
    private Guid? _conversationId;

    /// <summary>
    /// Sets the current conversation context, user ID, and conversation ID for this plugin instance.
    /// Called before kernel function execution.
    /// </summary>
    public void SetContext(ConversationContext context, Guid userId, Guid conversationId)
    {
        _context = context;
        _userId = userId;
        _conversationId = conversationId;
    }

    [KernelFunction]
    [Description("CreateAssessment: Creates a new assessment with a hypothesis, confidence level, differential diagnoses, reasoning, recommended action, and links to episodes and negative findings that informed this assessment.")]
    public async Task<string> CreateAssessmentAsync(
        [Description("The primary hypothesis or diagnosis")] string hypothesis,
        [Description("Confidence level from 0.0 to 1.0")] decimal confidence,
        [Description("Comma-separated list of alternative diagnoses")] string? differentials = null,
        [Description("Reasoning for this assessment")] string reasoning = "",
        [Description("Recommended action: self-care, see-gp, urgent-care, or emergency")] string recommendedAction = "see-gp",
        [Description("Comma-separated list of episode IDs")] string? episodeIds = null,
        [Description("Comma-separated list of negative finding IDs")] string? negativeFindingIds = null)
    {
        if (_context == null || !_conversationId.HasValue)
        {
            throw new InvalidOperationException("Context not set. Call SetContext before using plugin functions.");
        }

        try
        {
            var differentialsList = differentials != null
                ? differentials.Split(',').Select(d => d.Trim()).ToList()
                : null;

            var episodeIdsList = episodeIds != null
                ? episodeIds.Split(',').Select(id => int.TryParse(id.Trim(), out var eid) ? eid : (int?)null).Where(id => id.HasValue).Select(id => id!.Value).ToList()
                : _context.ActiveEpisodes.Select(e => e.Id).ToList();

            var negativeFindingIdsList = negativeFindingIds != null
                ? negativeFindingIds.Split(',').Select(id => int.TryParse(id.Trim(), out var nid) ? nid : (int?)null).Where(id => id.HasValue).Select(id => id!.Value).ToList()
                : _context.NegativeFindings.Select(nf => nf.Id).ToList();

            var assessment = new Assessment
            {
                UserId = _userId,
                ConversationId = _conversationId.Value,
                Hypothesis = hypothesis,
                Confidence = Math.Clamp(confidence, 0, 1),
                Differentials = differentialsList,
                Reasoning = reasoning,
                RecommendedAction = recommendedAction,
                EpisodeIds = episodeIdsList,
                NegativeFindingIds = negativeFindingIdsList
            };

            var created = await assessmentRepository.CreateAssessmentAsync(assessment);

            // Update context
            _context.CurrentAssessment = created;
            _context.Phase = ConversationPhase.Assessing;

            logger.LogInformation("Created assessment {AssessmentId} for conversation {ConversationId}", created.Id, _conversationId.Value);
            return $"Created assessment {created.Id}: {hypothesis} (confidence: {confidence:P0}). Recommended action: {recommendedAction}.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating assessment");
            return $"Error creating assessment: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("UpdateAssessment: Updates an existing assessment with new information, refining the hypothesis, confidence, or recommendations.")]
    public async Task<string> UpdateAssessmentAsync(
        [Description("The ID of the assessment to update")] int assessmentId,
        [Description("Updated hypothesis or diagnosis")] string? hypothesis = null,
        [Description("Updated confidence level from 0.0 to 1.0")] decimal? confidence = null,
        [Description("Updated comma-separated list of alternative diagnoses")] string? differentials = null,
        [Description("Updated reasoning")] string? reasoning = null,
        [Description("Updated recommended action")] string? recommendedAction = null,
        [Description("Updated comma-separated list of episode IDs")] string? episodeIds = null,
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

            List<int>? episodeIdsList = null;
            if (episodeIds != null)
            {
                episodeIdsList = episodeIds.Split(',').Select(id => int.TryParse(id.Trim(), out var eid) ? eid : (int?)null).Where(id => id.HasValue).Select(id => id!.Value).ToList();
            }

            List<int>? negativeFindingIdsList = null;
            if (negativeFindingIds != null)
            {
                negativeFindingIdsList = negativeFindingIds.Split(',').Select(id => int.TryParse(id.Trim(), out var nid) ? nid : (int?)null).Where(id => id.HasValue).Select(id => id!.Value).ToList();
            }

            var updated = await assessmentRepository.UpdateAssessmentAsync(
                assessmentId,
                hypothesis,
                confidence.HasValue ? Math.Clamp(confidence.Value, 0, 1) : null,
                differentialsList,
                reasoning,
                recommendedAction,
                episodeIdsList,
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
