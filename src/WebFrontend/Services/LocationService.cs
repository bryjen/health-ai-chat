using Web.Common.DTOs.Location;
using WebApi.ApiWrapper.Services;
using WebFrontend.Models.Location;

namespace WebFrontend.Services;

public class LocationService
{
    private readonly ILocationApiClient _apiClient;
    private List<CountryDto>? _countries;
    private readonly Dictionary<int, List<CityDto>> _citiesCache = new();
    private bool _isInitialized;

    public LocationService(ILocationApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    // Call this once on app startup or first use
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        try
        {
            _countries = await _apiClient.GetCountriesAsync();
            _isInitialized = true;
        }
        catch
        {
            // API error - initialize with empty list to prevent crashes
            _countries = new List<CountryDto>();
            // Don't set _isInitialized = true so it can retry on next call
        }
    }

    public async Task<List<CountryDto>> GetCountriesAsync()
    {
        if (!_isInitialized) await InitializeAsync();
        return _countries ?? new List<CountryDto>();
    }

    public async Task<List<StateDto>> GetStatesByCountryAsync(int countryId)
    {
        if (!_isInitialized) await InitializeAsync();
        return await _apiClient.GetStatesByCountryAsync(countryId);
    }

    // Loads cities for a specific state from backend API
    // Caches cities per state to avoid reloading if user switches states
    public async Task<List<CityDto>> GetCitiesByStateAsync(int stateId)
    {
        if (_citiesCache.TryGetValue(stateId, out var cachedCities))
            return cachedCities;

        try
        {
            var cities = await _apiClient.GetCitiesByStateAsync(stateId);
            _citiesCache[stateId] = cities;
            return cities;
        }
        catch
        {
            return new List<CityDto>();
        }
    }
}
