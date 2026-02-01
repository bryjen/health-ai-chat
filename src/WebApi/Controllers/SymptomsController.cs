using Microsoft.AspNetCore.Mvc;
using Web.Common.DTOs;
using Web.Common.DTOs.Health;
using WebApi.Controllers.Utils;
using WebApi.Repositories;

namespace WebApi.Controllers;

/// <summary>
/// Handles symptom-related endpoints
/// </summary>
[Route("api/v1/symptoms")]
[Produces("application/json")]
public class SymptomsController : BaseController
{
    /// <summary>
    /// Get all symptoms for the current user
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<SymptomDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<SymptomDto>>> GetSymptoms(
        [FromServices] SymptomRepository symptomRepository)
    {
        var userId = GetUserId();
        var symptoms = await symptomRepository.GetSymptomsWithEpisodeCountAsync(userId);

        var symptomDtos = symptoms.Select(s => new SymptomDto
        {
            Id = s.Id,
            Name = s.Name,
            Description = s.Description,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt,
            EpisodeCount = s.EpisodeCount
        }).ToList();

        return Ok(symptomDtos);
    }
}
