using System.Text.Json;
using System.Text.Json.Serialization;
using Web.Common.DTOs.Location;

namespace WebApi.Services.Location;

public class LocationService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<LocationService> _logger;
    private List<CountryDto>? _countries;
    private List<StateDto>? _allStates;
    private readonly Dictionary<int, List<CityDto>> _citiesCache = new();
    private bool _isInitialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    
    // JSON serializer options that match the JSON file format (snake_case property names)
    // JsonPropertyName attributes on DTOs handle the mapping explicitly
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // Don't set PropertyNamingPolicy - we use JsonPropertyName attributes instead
    };
    
    public LocationService(IWebHostEnvironment environment, ILogger<LocationService> logger)
    {
        _environment = environment;
        _logger = logger;
    }
    
    private string DataPath => Path.Combine(_environment.ContentRootPath, "Data", "location");
    
    // Call this once on app startup or first use
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        
        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized) return;
            
            var countriesPath = Path.Combine(DataPath, "countries.json");
            var statesPath = Path.Combine(DataPath, "states.json");
            
            if (!File.Exists(countriesPath) || !File.Exists(statesPath))
            {
                throw new FileNotFoundException($"Location data files not found. Expected: {countriesPath}, {statesPath}");
            }
            
            var countriesJson = await File.ReadAllTextAsync(countriesPath);
            var statesJson = await File.ReadAllTextAsync(statesPath);
            
            try
            {
                _countries = JsonSerializer.Deserialize<List<CountryDto>>(countriesJson, JsonOptions);
                _allStates = JsonSerializer.Deserialize<List<StateDto>>(statesJson, JsonOptions);
                
                if (_countries == null || _countries.Count == 0)
                {
                    _logger.LogError("Failed to deserialize countries.json. File exists: {Exists}, File size: {Size} bytes", 
                        File.Exists(countriesPath), countriesJson.Length);
                    _countries = new List<CountryDto>();
                }
                
                if (_allStates == null || _allStates.Count == 0)
                {
                    _logger.LogError("Failed to deserialize states.json. File exists: {Exists}, File size: {Size} bytes", 
                        File.Exists(statesPath), statesJson.Length);
                    _allStates = new List<StateDto>();
                }
                else
                {
                    // Log sample to verify deserialization worked
                    var sampleState = _allStates.FirstOrDefault();
                    if (sampleState != null)
                    {
                        _logger.LogInformation("Loaded {StateCount} states. Sample: Id={Id}, Name={Name}, CountryId={CountryId}", 
                            _allStates.Count, sampleState.Id, sampleState.Name, sampleState.CountryId);
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization error. Countries file size: {CountriesSize}, States file size: {StatesSize}", 
                    countriesJson.Length, statesJson.Length);
                throw;
            }
            
            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
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
        
        // Debug: Check if states are loaded and if any match
        var allStatesCount = _allStates?.Count ?? 0;
        var matchingStates = _allStates?.Where(s => s.CountryId == countryId).ToList() ?? new List<StateDto>();
        
        // If no states match, check if CountryId is being populated correctly
        if (matchingStates.Count == 0 && allStatesCount > 0)
        {
            // Sample a few states to see what CountryId values they have
            var sampleStates = _allStates?.Take(5).Select(s => $"StateId={s.Id}, CountryId={s.CountryId}, Name={s.Name}").ToList() ?? new List<string>();
            // This will help debug if CountryId is 0 for all states
        }
        
        return matchingStates;
    }
    
    // Loads cities for a specific state from cities/{stateId}.json (much smaller files)
    // Caches cities per state to avoid reloading if user switches states
    public async Task<List<CityDto>> GetCitiesByStateAsync(int stateId)
    {
        // Return cached cities if available
        if (_citiesCache.TryGetValue(stateId, out var cachedCities))
        {
            return cachedCities;
        }
        
        // Load from split files (cities/{stateId}.json)
        var citiesPath = Path.Combine(DataPath, "cities", $"{stateId}.json");
        
        if (!File.Exists(citiesPath))
        {
            return new List<CityDto>();
        }
        
        try
        {
            var citiesJson = await File.ReadAllTextAsync(citiesPath);
            var cities = JsonSerializer.Deserialize<List<CityDto>>(citiesJson, JsonOptions) ?? new List<CityDto>();
            
            // Cache the result
            _citiesCache[stateId] = cities;
            
            return cities;
        }
        catch (Exception)
        {
            // File read or deserialize error - return empty list
            return new List<CityDto>();
        }
    }
}
