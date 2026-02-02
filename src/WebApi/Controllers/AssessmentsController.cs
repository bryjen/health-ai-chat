using Microsoft.AspNetCore.Mvc;
using Web.Common.DTOs;
using Web.Common.DTOs.Health;
using WebApi.Controllers.Utils;
using WebApi.Repositories;
using WebApi.Services.Graph;

namespace WebApi.Controllers;

/// <summary>
/// Handles medical assessment endpoints.
/// Provides access to AI-generated health assessments, differential diagnoses, and recommended actions for conversations.
/// </summary>
[Route("api/v1/assessments")]
[Produces("application/json")]
public class AssessmentsController : BaseController
{
    /// <summary>
    /// Retrieves the medical assessment associated with a specific conversation.
    /// </summary>
    /// <param name="conversationId">The unique identifier of the conversation.</param>
    /// <returns>Assessment details including hypothesis, confidence, differentials, reasoning, and recommended actions.</returns>
    /// <response code="200">Assessment retrieved successfully.</response>
    /// <response code="401">User not authenticated.</response>
    /// <response code="404">Assessment not found or does not belong to the current user.</response>
    [HttpGet("conversation/{conversationId}")]
    [ProducesResponseType(typeof(AssessmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AssessmentDto>> GetAssessmentByConversation(
        [FromRoute] Guid conversationId,
        [FromServices] AssessmentRepository assessmentRepository)
    {
        var userId = GetUserId();
        var assessment = await assessmentRepository.GetAssessmentByConversationAsync(conversationId);

        if (assessment == null || assessment.UserId != userId)
        {
            return this.NotFoundError("Assessment not found");
        }

        var assessmentDto = new AssessmentDto
        {
            Id = assessment.Id,
            ConversationId = assessment.ConversationId,
            Hypothesis = assessment.Hypothesis,
            Confidence = assessment.Confidence,
            Differentials = assessment.Differentials,
            Reasoning = assessment.Reasoning,
            RecommendedAction = assessment.RecommendedAction,
            EpisodeIds = assessment.LinkedEpisodes?.Select(l => l.EpisodeId).ToList(), // Backward compatibility
            LinkedEpisodes = assessment.LinkedEpisodes?.Select(l => new AssessmentEpisodeLinkDto
            {
                EpisodeId = l.EpisodeId,
                Weight = l.Weight,
                Reasoning = l.Reasoning,
                EpisodeName = l.Episode?.Symptom?.Name
            }).ToList(),
            NegativeFindingIds = assessment.NegativeFindingIds,
            CreatedAt = assessment.CreatedAt
        };

        return Ok(assessmentDto);
    }

    /// <summary>
    /// Retrieves the most recent medical assessments for the current authenticated user.
    /// </summary>
    /// <param name="limit">Maximum number of assessments to return. Defaults to 10.</param>
    /// <returns>List of recent assessments with their details.</returns>
    /// <response code="200">Recent assessments retrieved successfully.</response>
    /// <response code="401">User not authenticated.</response>
    [HttpGet("recent")]
    [ProducesResponseType(typeof(List<AssessmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<AssessmentDto>>> GetRecentAssessments(
        [FromServices] AssessmentRepository assessmentRepository,
        [FromQuery] int limit = 10)
    {
        var userId = GetUserId();
        var assessments = await assessmentRepository.GetRecentAssessmentsAsync(userId, limit);

        var assessmentDtos = assessments.Select(a => new AssessmentDto
        {
            Id = a.Id,
            ConversationId = a.ConversationId,
            Hypothesis = a.Hypothesis,
            Confidence = a.Confidence,
            Differentials = a.Differentials,
            Reasoning = a.Reasoning,
            RecommendedAction = a.RecommendedAction,
            EpisodeIds = a.LinkedEpisodes?.Select(l => l.EpisodeId).ToList(), // Backward compatibility
            LinkedEpisodes = a.LinkedEpisodes?.Select(l => new AssessmentEpisodeLinkDto
            {
                EpisodeId = l.EpisodeId,
                Weight = l.Weight,
                Reasoning = l.Reasoning,
                EpisodeName = l.Episode?.Symptom?.Name
            }).ToList(),
            NegativeFindingIds = a.NegativeFindingIds,
            CreatedAt = a.CreatedAt
        }).ToList();

        return Ok(assessmentDtos);
    }

    /// <summary>
    /// Retrieves a specific medical assessment by its unique identifier.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(AssessmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AssessmentDto>> GetAssessmentById(
        [FromRoute] int id,
        [FromServices] AssessmentRepository assessmentRepository)
    {
        var userId = GetUserId();
        var assessment = await assessmentRepository.GetAssessmentByIdAsync(id);

        if (assessment == null || assessment.UserId != userId)
        {
            return this.NotFoundError("Assessment not found");
        }

        var assessmentDto = new AssessmentDto
        {
            Id = assessment.Id,
            ConversationId = assessment.ConversationId,
            Hypothesis = assessment.Hypothesis,
            Confidence = assessment.Confidence,
            Differentials = assessment.Differentials,
            Reasoning = assessment.Reasoning,
            RecommendedAction = assessment.RecommendedAction,
            EpisodeIds = assessment.LinkedEpisodes?.Select(l => l.EpisodeId).ToList(), // Backward compatibility
            LinkedEpisodes = assessment.LinkedEpisodes?.Select(l => new AssessmentEpisodeLinkDto
            {
                EpisodeId = l.EpisodeId,
                Weight = l.Weight,
                Reasoning = l.Reasoning,
                EpisodeName = l.Episode?.Symptom?.Name
            }).ToList(),
            NegativeFindingIds = assessment.NegativeFindingIds,
            CreatedAt = assessment.CreatedAt
        };

        return Ok(assessmentDto);
    }

    /// <summary>
    /// Retrieves graph visualization data for a specific assessment.
    /// </summary>
    /// <param name="id">The unique identifier of the assessment.</param>
    /// <returns>Graph data containing nodes and edges for visualization.</returns>
    /// <response code="200">Graph data retrieved successfully.</response>
    /// <response code="401">User not authenticated.</response>
    /// <response code="404">Assessment not found or does not belong to the current user.</response>
    [HttpGet("{id}/graph")]
    [ProducesResponseType(typeof(GraphDataDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<GraphDataDto>> GetAssessmentGraph(
        [FromRoute] int id,
        [FromServices] GraphDataService graphDataService)
    {
        var userId = GetUserId();
        
        try
        {
            var graphData = await graphDataService.GetAssessmentGraphDataAsync(id, userId);
            return Ok(graphData);
        }
        catch (InvalidOperationException)
        {
            return this.NotFoundError("Assessment not found");
        }
    }
}
