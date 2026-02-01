using System.Net.Http.Json;
using Web.Common.DTOs.Health;

namespace WebApi.ApiWrapper.Services;

/// <summary>
/// Implementation of <see cref="IEpisodesApiClient"/> for episodes API endpoints.
/// </summary>
public class EpisodesApiClient : BaseApiClient, IEpisodesApiClient
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EpisodesApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client for making requests.</param>
    /// <param name="tokenProvider">Token provider for authentication.</param>
    public EpisodesApiClient(HttpClient httpClient, ITokenProvider tokenProvider)
        : base(httpClient, tokenProvider)
    {
    }

    /// <inheritdoc/>
    public async Task<List<EpisodeDto>> GetActiveEpisodesAsync(int days = 14)
    {
        await EnsureAuthenticatedAsync();
        var response = await HttpClient.GetAsync($"api/v1/episodes/active?days={days}");

        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<List<EpisodeDto>>(BaseApiClient.JsonOptions);
        return result ?? new List<EpisodeDto>();
    }

    /// <inheritdoc/>
    public async Task<List<EpisodeDto>> GetEpisodesBySymptomAsync(string symptomName)
    {
        await EnsureAuthenticatedAsync();
        var encodedName = Uri.EscapeDataString(symptomName);
        var response = await HttpClient.GetAsync($"api/v1/episodes/symptom/{encodedName}");

        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<List<EpisodeDto>>(BaseApiClient.JsonOptions);
        return result ?? new List<EpisodeDto>();
    }

    /// <inheritdoc/>
    public async Task<EpisodeDto> GetEpisodeAsync(int episodeId)
    {
        await EnsureAuthenticatedAsync();
        var response = await HttpClient.GetAsync($"api/v1/episodes/{episodeId}");

        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<EpisodeDto>(BaseApiClient.JsonOptions);
        return result ?? throw new InvalidOperationException("Failed to deserialize episode response");
    }
}
