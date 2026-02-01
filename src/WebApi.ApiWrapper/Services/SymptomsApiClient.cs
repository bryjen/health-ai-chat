using System.Net.Http.Json;
using Web.Common.DTOs.Health;

namespace WebApi.ApiWrapper.Services;

/// <summary>
/// Implementation of <see cref="ISymptomsApiClient"/> for symptoms API endpoints.
/// </summary>
public class SymptomsApiClient : BaseApiClient, ISymptomsApiClient
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SymptomsApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client for making requests.</param>
    /// <param name="tokenProvider">Token provider for authentication.</param>
    public SymptomsApiClient(HttpClient httpClient, ITokenProvider tokenProvider)
        : base(httpClient, tokenProvider)
    {
    }

    /// <inheritdoc/>
    public async Task<List<SymptomDto>> GetSymptomsAsync()
    {
        await EnsureAuthenticatedAsync();
        var response = await HttpClient.GetAsync("api/v1/symptoms");

        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<List<SymptomDto>>(BaseApiClient.JsonOptions);
        return result ?? new List<SymptomDto>();
    }
}
