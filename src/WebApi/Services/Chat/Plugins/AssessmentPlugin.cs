using System.ComponentModel;
using Microsoft.Extensions.AI;
using WebApi.Models;
using WebApi.Services.Data;

namespace WebApi.Services.Chat.Plugins;

public sealed class AssessmentPlugin(
    AiState aiState,
    AssessmentService assessmentService,
    ILogger<AssessmentPlugin> logger)
{
    [Description("Create a new assessment with a diagnosis hypothesis, confidence, and recommendations.")]
    public async Task<string> CreateAssessmentAsync(
        [Description("The primary hypothesis or diagnosis")] string hypothesis,
        [Description("Confidence level from 0.0 to 1.0")] float confidence,
        [Description("Comma-separated list of alternative diagnoses")] string? differentials = null,
        [Description("Reasoning and clinical notes")] string reasoning = "",
        [Description("Recommended action: self-care, see-gp, urgent-care, or emergency")] string recommendedAction = "see-gp")
    {
        try
        {
            if (aiState.ConversationId is null)
                return "Error: ConversationId is required in AiState.";

            var differentialsList = differentials?
                .Split(',')
                .Select(d => d.Trim())
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .ToList();

            var assessment = new Assessment
            {
                UserId = aiState.UserId,
                ConversationId = aiState.ConversationId.Value,
                Hypothesis = hypothesis,
                Confidence = (decimal)Math.Clamp(confidence, 0, 1),
                Differentials = differentialsList,
                Reasoning = reasoning,
                RecommendedAction = recommendedAction
            };

            var created = await assessmentService.CreateAssessmentAsync(assessment);

            logger.LogInformation("Created assessment {AssessmentId}: {Hypothesis} (confidence: {Confidence})",
                created.Id, hypothesis, confidence);

            return $"Created assessment {created.Id}: {hypothesis} (confidence: {confidence:P0}). Recommended action: {recommendedAction}.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating assessment");
            return $"Error: {ex.Message}";
        }
    }

    [Description("Update an existing assessment with refined information.")]
    public async Task<string> UpdateAssessmentAsync(
        [Description("Assessment ID")] int assessmentId,
        [Description("Updated hypothesis")] string? hypothesis = null,
        [Description("Updated confidence level (0.0-1.0), or -1 to leave unchanged")] float confidence = -1,
        [Description("Updated comma-separated alternative diagnoses")] string? differentials = null,
        [Description("Updated reasoning")] string? reasoning = null,
        [Description("Updated recommended action")] string? recommendedAction = null)
    {
        try
        {
            var differentialsList = differentials?
                .Split(',')
                .Select(d => d.Trim())
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .ToList();

            var clampedConfidence = confidence >= 0 ? (decimal?)Math.Clamp(confidence, 0, 1) : null;

            var updated = await assessmentService.UpdateAssessmentAsync(
                assessmentId,
                hypothesis,
                clampedConfidence,
                differentialsList,
                reasoning,
                recommendedAction);

            if (updated == null)
                return $"Assessment {assessmentId} not found.";

            logger.LogInformation("Updated assessment {AssessmentId}", assessmentId);
            return $"Updated assessment {assessmentId}.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating assessment");
            return $"Error: {ex.Message}";
        }
    }

    [Description("Get a specific assessment by ID.")]
    public async Task<string> GetAssessmentAsync(
        [Description("Assessment ID")] int assessmentId)
    {
        try
        {
            var assessment = await assessmentService.GetAssessmentByIdAsync(assessmentId);

            if (assessment == null)
                return $"Assessment {assessmentId} not found.";

            var linkedEpisodes = assessment.LinkedEpisodes.Any()
                ? string.Join(", ", assessment.LinkedEpisodes.Select(le => $"Episode {le.EpisodeId}"))
                : "none";

            return $"Assessment {assessmentId}:\n" +
                   $"- Hypothesis: {assessment.Hypothesis}\n" +
                   $"- Confidence: {assessment.Confidence:P0}\n" +
                   $"- Recommended action: {assessment.RecommendedAction}\n" +
                   $"- Linked episodes: {linkedEpisodes}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting assessment");
            return $"Error: {ex.Message}";
        }
    }

    [Description("Get the user's recent assessments.")]
    public async Task<string> GetRecentAssessmentsAsync(
        [Description("Number of recent assessments to retrieve (default: 5)")] float limit = 5)
    {
        try
        {
            var assessments = await assessmentService.GetRecentAssessmentsAsync(aiState.UserId, (int)limit);

            if (!assessments.Any())
                return "No recent assessments found.";

            var summary = string.Join("\n", assessments.Select(a =>
                $"- Assessment {a.Id}: {a.Hypothesis} (confidence: {a.Confidence:P0}, action: {a.RecommendedAction})"));

            return $"Recent assessments:\n{summary}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting recent assessments");
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Returns the functions provided by this plugin.
    /// </summary>
    public IEnumerable<AITool> AsAiTools()
    {
        yield return AIFunctionFactory.Create(CreateAssessmentAsync);
        yield return AIFunctionFactory.Create(UpdateAssessmentAsync);
        yield return AIFunctionFactory.Create(GetAssessmentAsync);
        yield return AIFunctionFactory.Create(GetRecentAssessmentsAsync);
    }
}
