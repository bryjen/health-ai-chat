using System.Net.Http.Json;
using Web.Common.DTOs.Health;

namespace WebApi.ApiWrapper.Services;

/// <summary>
/// Implementation of <see cref="IHealthChatApiClient"/> for health chat API endpoints.
/// </summary>
public class HealthChatApiClient : BaseApiClient, IHealthChatApiClient
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HealthChatApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client for making requests.</param>
    /// <param name="tokenProvider">Optional token provider for authentication.</param>
    public HealthChatApiClient(HttpClient httpClient, ITokenProvider? tokenProvider = null)
        : base(httpClient, tokenProvider)
    {
    }

    /// <inheritdoc/>
    public async Task<HealthChatResponse> SendHealthMessageAsync(HealthChatRequest request)
    {
        await EnsureAuthenticatedAsync();
        
        var response = await HttpClient.PostAsJsonAsync("api/v1/health/chat", request, BaseApiClient.JsonOptions);
        
        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<HealthChatResponse>(BaseApiClient.JsonOptions);
        return result ?? throw new Exceptions.ApiException("Failed to deserialize health chat response", 500);
    }
}
