using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web.Common.DTOs;
using Web.Common.DTOs.Health;
using WebApi.Controllers.Utils;
using WebApi.Data;
using WebApi.Repositories;

namespace WebApi.Controllers;

/// <summary>
/// Handles episode-related endpoints for symptom tracking
/// </summary>
[Route("api/v1/episodes")]
[Produces("application/json")]
public class EpisodesController : BaseController
{
    /// <summary>
    /// Get active episodes for the current user
    /// </summary>
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
    /// Get a specific episode by ID
    /// </summary>
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
