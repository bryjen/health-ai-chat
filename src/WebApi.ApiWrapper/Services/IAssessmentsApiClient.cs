using Web.Common.DTOs.Health;

namespace WebApi.ApiWrapper.Services;

/// <summary>
/// Client interface for assessments API endpoints.
/// </summary>
public interface IAssessmentsApiClient
{
    /// <summary>
    /// Gets assessment for a specific conversation.
    /// </summary>
    /// <param name="conversationId">The conversation ID</param>
    /// <returns>The assessment details</returns>
    /// <exception cref="Exceptions.ApiNotFoundException">Thrown when assessment is not found (404).</exception>
    /// <exception cref="Exceptions.ApiException">Thrown for other API errors.</exception>
    Task<AssessmentDto> GetAssessmentByConversationAsync(Guid conversationId);

    /// <summary>
    /// Gets recent assessments for the current user.
    /// </summary>
    /// <param name="limit">Maximum number of assessments to return (default: 10)</param>
    /// <returns>List of recent assessments</returns>
    /// <exception cref="Exceptions.ApiException">Thrown for API errors.</exception>
    Task<List<AssessmentDto>> GetRecentAssessmentsAsync(int limit = 10);
}
