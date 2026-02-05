using WebApi.Hubs;
using WebApi.Models;
using WebApi.Repositories;
using WebApi.Services.AI.Workflows;
using WebApi.Services.Chat.Conversations;

namespace WebApi.Services.AI.Tools;

/// <summary>
/// Agent Framework tools for assessment operations.
/// Converted from AssessmentPlugin - provides tools that workflows can call.
/// </summary>
public class AssessmentTools(
    AssessmentRepository assessmentRepository,
    ConversationContextService contextService,
    ILogger<AssessmentTools> logger)
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

    /// <summary>
    /// Creates an assessment. Call this when user requests an assessment, diagnosis, or evaluation.
    /// </summary>
    public async Task<AssessmentResult> CreateAssessmentAsync(
        string hypothesis,
        decimal confidence,
        List<string>? differentials = null,
        string reasoning = "",
        string recommendedAction = "see-gp",
        List<int>? negativeFindingIds = null)
    {
        logger.LogInformation("[ASSESSMENT_TOOLS] CreateAssessmentAsync CALLED with hypothesis='{Hypothesis}', confidence={Confidence}, recommendedAction='{RecommendedAction}'",
            hypothesis, confidence, recommendedAction);
        try
        {
            var context = GetContext();
            var clientConnection = GetClientConnection();

            if (!context.ConversationId.HasValue)
            {
                logger.LogError("Cannot create assessment: ConversationId is null. UserId: {UserId}", context.UserId);
                return new AssessmentResult
                {
                    NextRecommendedAction = "SubmitFinalResponse",
                    ErrorMessage = "Cannot create assessment because no conversation is active. Please start a conversation first."
                };
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

            var resultAsStr = $"Created assessment {created.Id}: {hypothesis} (confidence: {confidence:P0}). Recommended action (for the user): {recommendedAction}.";
            logger.LogInformation(resultAsStr);

            return new AssessmentResult
            {
                NextRecommendedAction = "CompleteAssessment",
                CreatedAssessment = created
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating assessment. Exception type: {ExceptionType}, Message: {Message}, StackTrace: {StackTrace}",
                ex.GetType().Name, ex.Message, ex.StackTrace);

            // Log the actual parameters that were passed for debugging
            logger.LogError("CreateAssessment parameters - Hypothesis: {Hypothesis}, Confidence: {Confidence}, Differentials: {Differentials}, Reasoning: {Reasoning}, RecommendedAction: {RecommendedAction}",
                hypothesis, confidence, differentials != null ? string.Join(", ", differentials) : "null", reasoning, recommendedAction);

            // Return a more helpful error message
            var errorMessage = "Failed to create assessment due to a technical error. Please try again or contact support if the issue persists.";
            return new AssessmentResult
            {
                NextRecommendedAction = "SubmitFinalResponse",
                ErrorMessage = errorMessage
            };
        }
    }

    /// <summary>
    /// Updates an existing assessment when you need to refine or change it.
    /// </summary>
    public async Task<AssessmentResult> UpdateAssessmentAsync(
        int assessmentId,
        string? hypothesis = null,
        decimal? confidence = null,
        List<string>? differentials = null,
        string? reasoning = null,
        string? recommendedAction = null,
        List<EpisodeWeight>? episodeWeights = null,
        List<int>? negativeFindingIds = null)
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
                return new AssessmentResult
                {
                    NextRecommendedAction = "SubmitFinalResponse",
                    ErrorMessage = $"Assessment {assessmentId} not found."
                };
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
            return new AssessmentResult
            {
                NextRecommendedAction = "Continue",
                UpdatedAssessment = updated
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating assessment {AssessmentId}", assessmentId);
            return new AssessmentResult
            {
                NextRecommendedAction = "SubmitFinalResponse",
                ErrorMessage = $"Error updating assessment: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Completes an assessment. Call this immediately after CreateAssessment to finalize it.
    /// </summary>
    public async Task<AssessmentResult> CompleteAssessmentAsync(int? assessmentId = null)
    {
        logger.LogInformation("[ASSESSMENT_TOOLS] CompleteAssessmentAsync CALLED with assessmentId={AssessmentId}", assessmentId);
        try
        {
            var context = GetContext();
            var clientConnection = GetClientConnection();

            if (!context.ConversationId.HasValue)
            {
                logger.LogError("Cannot complete assessment: ConversationId is null. UserId: {UserId}", context.UserId);
                return new AssessmentResult
                {
                    NextRecommendedAction = "SubmitFinalResponse",
                    ErrorMessage = "Cannot complete assessment because no conversation is active."
                };
            }

            // Determine which assessment to complete
            int targetAssessmentId;
            if (assessmentId.HasValue)
            {
                targetAssessmentId = assessmentId.Value;
            }
            else if (context.CurrentAssessment != null)
            {
                targetAssessmentId = context.CurrentAssessment.Id;
            }
            else
            {
                logger.LogError("Cannot complete assessment: No assessment ID provided and no current assessment exists");
                return new AssessmentResult
                {
                    NextRecommendedAction = "SubmitFinalResponse",
                    ErrorMessage = "No assessment found to complete. Please create an assessment first."
                };
            }

            // Update conversation phase to Recommending
            context.Phase = ConversationPhase.Recommending;

            // Send status update (fire-and-forget)
            clientConnection?.SendAssessmentComplete(targetAssessmentId, $"Assessment {targetAssessmentId} completed.");

            logger.LogInformation("Completed assessment {AssessmentId} for conversation {ConversationId}", targetAssessmentId, context.ConversationId.Value);
            return new AssessmentResult
            {
                NextRecommendedAction = "Continue",
                CompletedAssessmentId = targetAssessmentId
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error completing assessment. Exception type: {ExceptionType}, Message: {Message}",
                ex.GetType().Name, ex.Message);
            return new AssessmentResult
            {
                NextRecommendedAction = "SubmitFinalResponse",
                ErrorMessage = $"Failed to complete assessment due to a technical error: {ex.Message}"
            };
        }
    }
}
