using Microsoft.AspNetCore.Mvc;
using Web.Common.DTOs;
using Web.Common.DTOs.Health;
using WebApi.Controllers.Utils;
using WebApi.Repositories;
using WebApi.Services.Graph;

namespace WebApi.Controllers;

/// <summary>
/// Handles assessment-related endpoints
/// </summary>
[Route("api/v1/assessments")]
[Produces("application/json")]
public class AssessmentsController : BaseController
{
    /// <summary>
    /// Get assessment for a specific conversation
    /// </summary>
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
    /// Get recent assessments for the current user
    /// </summary>
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
    /// Get assessment by ID
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
    /// Get graph data for a specific assessment
    /// </summary>
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
