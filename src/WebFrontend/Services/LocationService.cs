using WebApi.ApiWrapper.Services;
using WebFrontend.Models.Location;

namespace WebFrontend.Services;

public class LocationService
{
    private readonly ILocationApiClient _apiClient;
    private List<Country>? _countries;
    private readonly Dictionary<int, List<City>> _citiesCache = new();
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
            var countryDtos = await _apiClient.GetCountriesAsync();
            
            // Cache countries - states will be loaded per country on demand
            _countries = countryDtos.Select(dto => new Country
            {
                Id = dto.Id,
                Name = dto.Name,
                Iso2 = dto.Iso2
            }).ToList();
            
            _isInitialized = true;
        }
        catch
        {
            // API error - initialize with empty list to prevent crashes
            _countries = new List<Country>();
            // Don't set _isInitialized = true so it can retry on next call
        }
    }
    
    public async Task<List<Country>> GetCountriesAsync()
    {
        if (!_isInitialized) await InitializeAsync();
        return _countries ?? new List<Country>();
    }
    
    public async Task<List<State>> GetStatesByCountryAsync(int countryId)
    {
        if (!_isInitialized) await InitializeAsync();
        
        // Load states from API for this country
        var stateDtos = await _apiClient.GetStatesByCountryAsync(countryId);
        return stateDtos.Select(dto => new State
        {
            Id = dto.Id,
            Name = dto.Name,
            CountryId = dto.CountryId,
            CountryCode = dto.CountryCode
        }).ToList();
    }
    
    // Loads cities for a specific state from backend API
    // Caches cities per state to avoid reloading if user switches states
    public async Task<List<City>> GetCitiesByStateAsync(int stateId)
    {
        // Return cached cities if available
        if (_citiesCache.TryGetValue(stateId, out var cachedCities))
        {
            return cachedCities;
        }
        
        // Load from backend API
        try
        {
            var cityDtos = await _apiClient.GetCitiesByStateAsync(stateId);
            var cities = cityDtos.Select(dto => new City
            {
                Id = dto.Id,
                Name = dto.Name,
                StateId = dto.StateId,
                CountryId = dto.CountryId
            }).ToList();
            
            // Cache the result
            _citiesCache[stateId] = cities;
            
            return cities;
        }
        catch
        {
            // API error - return empty list
            return new List<City>();
        }
    }
}
