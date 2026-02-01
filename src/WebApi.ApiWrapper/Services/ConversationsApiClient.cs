using System.Net.Http.Json;
using Web.Common.DTOs.Conversations;

namespace WebApi.ApiWrapper.Services;

/// <summary>
/// Implementation of <see cref="IConversationsApiClient"/> for conversations API endpoints.
/// </summary>
public class ConversationsApiClient : BaseApiClient, IConversationsApiClient
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationsApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client for making requests.</param>
    /// <param name="tokenProvider">Optional token provider for authentication.</param>
    public ConversationsApiClient(HttpClient httpClient, ITokenProvider? tokenProvider = null)
        : base(httpClient, tokenProvider)
    {
    }

    /// <inheritdoc/>
    public async Task<List<ConversationSummaryDto>> GetAllConversationsAsync()
    {
        await EnsureAuthenticatedAsync();
        
        var response = await HttpClient.GetAsync("api/v1/conversations");
        
        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<List<ConversationSummaryDto>>(BaseApiClient.JsonOptions);
        return result ?? new List<ConversationSummaryDto>();
    }

    /// <inheritdoc/>
    public async Task<ConversationDto?> GetConversationByIdAsync(Guid id)
    {
        await EnsureAuthenticatedAsync();
        
        var response = await HttpClient.GetAsync($"api/v1/conversations/{id}");
        
        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<ConversationDto>(BaseApiClient.JsonOptions);
    }

    /// <inheritdoc/>
    public async Task<ConversationDto> UpdateConversationTitleAsync(Guid id, UpdateConversationTitleRequest request)
    {
        await EnsureAuthenticatedAsync();
        
        var response = await HttpClient.PutAsJsonAsync($"api/v1/conversations/{id}", request, BaseApiClient.JsonOptions);
        
        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<ConversationDto>();
        return result ?? throw new Exceptions.ApiException("Failed to deserialize conversation response", 500);
    }

    /// <inheritdoc/>
    public async Task DeleteConversationAsync(Guid id)
    {
        await EnsureAuthenticatedAsync();
        
        var response = await HttpClient.DeleteAsync($"api/v1/conversations/{id}");
        
        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }
    }
}
