using System.Net.Http.Json;
using Web.Common.DTOs.Health;

namespace WebApi.ApiWrapper.Services;

/// <summary>
/// Implementation of <see cref="IAssessmentsApiClient"/> for assessments API endpoints.
/// </summary>
public class AssessmentsApiClient : BaseApiClient, IAssessmentsApiClient
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AssessmentsApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client for making requests.</param>
    /// <param name="tokenProvider">Token provider for authentication.</param>
    public AssessmentsApiClient(HttpClient httpClient, ITokenProvider tokenProvider)
        : base(httpClient, tokenProvider)
    {
    }

    /// <inheritdoc/>
    public async Task<AssessmentDto> GetAssessmentByConversationAsync(Guid conversationId)
    {
        await EnsureAuthenticatedAsync();
        var response = await HttpClient.GetAsync($"api/v1/assessments/conversation/{conversationId}");

        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<AssessmentDto>(BaseApiClient.JsonOptions);
        return result ?? throw new InvalidOperationException("Failed to deserialize assessment response");
    }

    /// <inheritdoc/>
    public async Task<List<AssessmentDto>> GetRecentAssessmentsAsync(int limit = 10)
    {
        await EnsureAuthenticatedAsync();
        var response = await HttpClient.GetAsync($"api/v1/assessments/recent?limit={limit}");

        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<List<AssessmentDto>>(BaseApiClient.JsonOptions);
        return result ?? new List<AssessmentDto>();
    }

    /// <inheritdoc/>
    public async Task<AssessmentDto> GetAssessmentByIdAsync(int id)
    {
        await EnsureAuthenticatedAsync();
        var response = await HttpClient.GetAsync($"api/v1/assessments/{id}");

        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<AssessmentDto>(BaseApiClient.JsonOptions);
        return result ?? throw new InvalidOperationException("Failed to deserialize assessment response");
    }

    /// <inheritdoc/>
    public async Task<GraphDataDto> GetAssessmentGraphAsync(int id)
    {
        await EnsureAuthenticatedAsync();
        var response = await HttpClient.GetAsync($"api/v1/assessments/{id}/graph");

        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<GraphDataDto>(BaseApiClient.JsonOptions);
        return result ?? throw new InvalidOperationException("Failed to deserialize graph data response");
    }
}
