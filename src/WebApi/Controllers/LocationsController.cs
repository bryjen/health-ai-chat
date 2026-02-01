using Microsoft.AspNetCore.Mvc;
using Web.Common.DTOs;
using Web.Common.DTOs.Location;
using WebApi.Controllers.Utils;
using WebApi.Services.Location;

namespace WebApi.Controllers;

/// <summary>
/// Provides location data endpoints for countries, states, and cities.
/// </summary>
[Route("api/v1/locations")]
[Produces("application/json")]
public class LocationsController : ControllerBase
{
    private readonly LocationService _locationService;
    private readonly ILogger<LocationsController> _logger;
    
    public LocationsController(LocationService locationService, ILogger<LocationsController> logger)
    {
        _locationService = locationService;
        _logger = logger;
    }
    
    /// <summary>
    /// Gets all available countries.
    /// </summary>
    /// <returns>List of all countries</returns>
    /// <response code="200">Returns the list of countries</response>
    [HttpGet("countries")]
    [ProducesResponseType(typeof(List<CountryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CountryDto>>> GetCountries()
    {
        try
        {
            var countries = await _locationService.GetCountriesAsync();
            return Ok(countries);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(ex, "Location data files not found");
            return this.BadRequestError("Location data not available");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading countries");
            return this.BadRequestError("An error occurred while loading countries");
        }
    }
    
    /// <summary>
    /// Gets all states/provinces for a specific country.
    /// </summary>
    /// <param name="countryId">The country ID to get states for</param>
    /// <returns>List of states for the specified country</returns>
    /// <response code="200">Returns the list of states</response>
    /// <response code="400">Invalid country ID</response>
    [HttpGet("states")]
    [ProducesResponseType(typeof(List<StateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<StateDto>>> GetStates([FromQuery] int countryId)
    {
        if (countryId <= 0)
        {
            return this.BadRequestError("Valid countryId is required");
        }
        
        try
        {
            var states = await _locationService.GetStatesByCountryAsync(countryId);
            return Ok(states);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading states for country {CountryId}", countryId);
            return this.BadRequestError("An error occurred while loading states");
        }
    }
    
    /// <summary>
    /// Gets all cities for a specific state/province.
    /// </summary>
    /// <param name="stateId">The state ID to get cities for</param>
    /// <returns>List of cities for the specified state</returns>
    /// <response code="200">Returns the list of cities</response>
    /// <response code="400">Invalid state ID</response>
    [HttpGet("cities")]
    [ProducesResponseType(typeof(List<CityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<CityDto>>> GetCities([FromQuery] int stateId)
    {
        if (stateId <= 0)
        {
            return this.BadRequestError("Valid stateId is required");
        }
        
        try
        {
            var cities = await _locationService.GetCitiesByStateAsync(stateId);
            return Ok(cities);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading cities for state {StateId}", stateId);
            return this.BadRequestError("An error occurred while loading cities");
        }
    }
}
