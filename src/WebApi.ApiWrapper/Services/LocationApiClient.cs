using System.Net.Http.Json;
using System.Text.Json;
using Web.Common.DTOs.Location;

namespace WebApi.ApiWrapper.Services;

/// <summary>
/// Implementation of <see cref="ILocationApiClient"/> for location API endpoints.
/// </summary>
public class LocationApiClient : BaseApiClient, ILocationApiClient
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LocationApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client for making requests.</param>
    /// <param name="tokenProvider">Optional token provider for authentication (not required for public endpoints).</param>
    public LocationApiClient(HttpClient httpClient, ITokenProvider? tokenProvider = null)
        : base(httpClient, tokenProvider)
    {
    }
    
    /// <inheritdoc/>
    public async Task<List<CountryDto>> GetCountriesAsync()
    {
        var response = await HttpClient.GetAsync("api/v1/locations/countries");
        
        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }
        
        var result = await response.Content.ReadFromJsonAsync<List<CountryDto>>(BaseApiClient.JsonOptions);
        return result ?? new List<CountryDto>();
    }
    
    /// <inheritdoc/>
    public async Task<List<StateDto>> GetStatesByCountryAsync(int countryId)
    {
        var response = await HttpClient.GetAsync($"api/v1/locations/states?countryId={countryId}");
        
        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }
        
        var result = await response.Content.ReadFromJsonAsync<List<StateDto>>(BaseApiClient.JsonOptions);
        return result ?? new List<StateDto>();
    }
    
    /// <inheritdoc/>
    public async Task<List<CityDto>> GetCitiesByStateAsync(int stateId)
    {
        var response = await HttpClient.GetAsync($"api/v1/locations/cities?stateId={stateId}");
        
        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }
        
        var result = await response.Content.ReadFromJsonAsync<List<CityDto>>(BaseApiClient.JsonOptions);
        return result ?? new List<CityDto>();
    }
}
