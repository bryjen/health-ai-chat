using Web.Common.DTOs.Health;

namespace WebApi.ApiWrapper.Services;

/// <summary>
/// Client interface for symptoms API endpoints.
/// </summary>
public interface ISymptomsApiClient
{
    /// <summary>
    /// Gets all symptoms for the current user.
    /// </summary>
    /// <returns>List of symptoms</returns>
    /// <exception cref="Exceptions.ApiException">Thrown for API errors.</exception>
    Task<List<SymptomDto>> GetSymptomsAsync();
}
