using Web.Common.DTOs.Health;

namespace WebApi.ApiWrapper.Services;

/// <summary>
/// Client interface for episodes API endpoints.
/// </summary>
public interface IEpisodesApiClient
{
    /// <summary>
    /// Gets active episodes for the current user.
    /// </summary>
    /// <param name="days">Number of days to look back (default: 14)</param>
    /// <returns>List of active episodes</returns>
    /// <exception cref="Exceptions.ApiException">Thrown for API errors.</exception>
    Task<List<EpisodeDto>> GetActiveEpisodesAsync(int days = 14);

    /// <summary>
    /// Gets all episodes for a specific symptom type.
    /// </summary>
    /// <param name="symptomName">The symptom name to get episodes for</param>
    /// <returns>List of episodes for the specified symptom</returns>
    /// <exception cref="Exceptions.ApiException">Thrown for API errors.</exception>
    Task<List<EpisodeDto>> GetEpisodesBySymptomAsync(string symptomName);

    /// <summary>
    /// Gets a specific episode by ID.
    /// </summary>
    /// <param name="episodeId">The episode ID</param>
    /// <returns>The episode details</returns>
    /// <exception cref="Exceptions.ApiNotFoundException">Thrown when episode is not found (404).</exception>
    /// <exception cref="Exceptions.ApiException">Thrown for other API errors.</exception>
    Task<EpisodeDto> GetEpisodeAsync(int episodeId);
}
