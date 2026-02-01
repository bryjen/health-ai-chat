using Web.Common.DTOs.Location;

namespace WebApi.ApiWrapper.Services;

/// <summary>
/// Client interface for location API endpoints.
/// </summary>
public interface ILocationApiClient
{
    /// <summary>
    /// Gets all available countries.
    /// </summary>
    /// <returns>List of all countries</returns>
    /// <exception cref="Exceptions.ApiException">Thrown for API errors.</exception>
    Task<List<CountryDto>> GetCountriesAsync();

    /// <summary>
    /// Gets all states/provinces for a specific country.
    /// </summary>
    /// <param name="countryId">The country ID to get states for</param>
    /// <returns>List of states for the specified country</returns>
    /// <exception cref="Exceptions.ApiValidationException">Thrown when countryId is invalid (400).</exception>
    /// <exception cref="Exceptions.ApiException">Thrown for other API errors.</exception>
    Task<List<StateDto>> GetStatesByCountryAsync(int countryId);

    /// <summary>
    /// Gets all cities for a specific state/province.
    /// </summary>
    /// <param name="stateId">The state ID to get cities for</param>
    /// <returns>List of cities for the specified state</returns>
    /// <exception cref="Exceptions.ApiValidationException">Thrown when stateId is invalid (400).</exception>
    /// <exception cref="Exceptions.ApiException">Thrown for other API errors.</exception>
    Task<List<CityDto>> GetCitiesByStateAsync(int stateId);
}
