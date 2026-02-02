using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web.Common.DTOs;
using Web.Common.DTOs.Health;
using WebApi.Controllers.Utils;
using WebApi.Data;
using WebApi.Repositories;

namespace WebApi.Controllers;

/// <summary>
/// Handles episode-related endpoints for symptom tracking.
/// Provides access to symptom episodes with their timelines, severity, triggers, and resolution status.
/// </summary>
[Route("api/v1/episodes")]
[Produces("application/json")]
public class EpisodesController : BaseController
{
    /// <summary>
    /// Retrieves active symptom episodes for the current authenticated user.
    /// </summary>
    /// <param name="days">Number of days to look back for active episodes. Defaults to 14 days.</param>
    /// <returns>List of active episodes with their details, timelines, and symptom information.</returns>
    /// <response code="200">Active episodes retrieved successfully.</response>
    /// <response code="401">User not authenticated.</response>
    [HttpGet("active")]
    [ProducesResponseType(typeof(List<EpisodeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<EpisodeDto>>> GetActiveEpisodes(
        [FromServices] EpisodeRepository episodeRepository,
        [FromServices] AppDbContext context,
        [FromQuery] int days = 14)
    {
        var userId = GetUserId();
        var episodes = await episodeRepository.GetActiveEpisodesAsync(userId, days);

        var episodeDtos = episodes.Select(e => new EpisodeDto
        {
            Id = e.Id,
            SymptomId = e.SymptomId,
            SymptomName = e.Symptom?.Name ?? "Unknown",
            SymptomDescription = e.Symptom?.Description,
            Stage = e.Stage,
            Status = e.Status,
            StartedAt = e.StartedAt,
            ResolvedAt = e.ResolvedAt,
            Severity = e.Severity,
            Location = e.Location,
            Frequency = e.Frequency,
            Triggers = e.Triggers,
            Relievers = e.Relievers,
            Pattern = e.Pattern,
            Timeline = e.Timeline?.Select(t => new EpisodeTimelineEntryDto
            {
                Date = t.Date,
                Severity = t.Severity,
                Notes = t.Notes
            }).ToList(),
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt
        }).ToList();

        return Ok(episodeDtos);
    }

    /// <summary>
    /// Get all episodes for a specific symptom type
    /// </summary>
    [HttpGet("symptom/{symptomName}")]
    [ProducesResponseType(typeof(List<EpisodeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<EpisodeDto>>> GetEpisodesBySymptom(
        [FromRoute] string symptomName,
        [FromServices] EpisodeRepository episodeRepository)
    {
        var userId = GetUserId();
        var episodes = await episodeRepository.GetEpisodesBySymptomAsync(userId, symptomName);

        var episodeDtos = episodes.Select(e => new EpisodeDto
        {
            Id = e.Id,
            SymptomId = e.SymptomId,
            SymptomName = e.Symptom?.Name ?? "Unknown",
            SymptomDescription = e.Symptom?.Description,
            Stage = e.Stage,
            Status = e.Status,
            StartedAt = e.StartedAt,
            ResolvedAt = e.ResolvedAt,
            Severity = e.Severity,
            Location = e.Location,
            Frequency = e.Frequency,
            Triggers = e.Triggers,
            Relievers = e.Relievers,
            Pattern = e.Pattern,
            Timeline = e.Timeline?.Select(t => new EpisodeTimelineEntryDto
            {
                Date = t.Date,
                Severity = t.Severity,
                Notes = t.Notes
            }).ToList(),
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt
        }).ToList();

        return Ok(episodeDtos);
    }

    /// <summary>
    /// Retrieves a specific symptom episode by its unique identifier.
    /// </summary>
    /// <param name="episodeId">The unique identifier of the episode.</param>
    /// <returns>Complete episode details with symptom information and timeline.</returns>
    /// <response code="200">Episode retrieved successfully.</response>
    /// <response code="401">User not authenticated.</response>
    /// <response code="404">Episode not found or does not belong to the current user.</response>
    [HttpGet("{episodeId}")]
    [ProducesResponseType(typeof(EpisodeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<EpisodeDto>> GetEpisode(
        [FromRoute] int episodeId,
        [FromServices] EpisodeRepository episodeRepository)
    {
        var userId = GetUserId();
        var episode = await episodeRepository.GetEpisodeAsync(episodeId);

        if (episode == null || episode.UserId != userId)
        {
            return this.NotFoundError("Episode not found");
        }

        var episodeDto = new EpisodeDto
        {
            Id = episode.Id,
            SymptomId = episode.SymptomId,
            SymptomName = episode.Symptom?.Name ?? "Unknown",
            SymptomDescription = episode.Symptom?.Description,
            Stage = episode.Stage,
            Status = episode.Status,
            StartedAt = episode.StartedAt,
            ResolvedAt = episode.ResolvedAt,
            Severity = episode.Severity,
            Location = episode.Location,
            Frequency = episode.Frequency,
            Triggers = episode.Triggers,
            Relievers = episode.Relievers,
            Pattern = episode.Pattern,
            Timeline = episode.Timeline?.Select(t => new EpisodeTimelineEntryDto
            {
                Date = t.Date,
                Severity = t.Severity,
                Notes = t.Notes
            }).ToList(),
            CreatedAt = episode.CreatedAt,
            UpdatedAt = episode.UpdatedAt
        };

        return Ok(episodeDto);
    }
}
