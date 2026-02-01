using Web.Common.DTOs.Health;

namespace WebApi.ApiWrapper.Services;

/// <summary>
/// Client interface for health chat API endpoints.
/// </summary>
public interface IHealthChatApiClient
{
    /// <summary>
    /// Sends a healthcare chat message. Processes the message with AI, tracks symptoms, and handles appointment booking.
    /// </summary>
    /// <param name="request">Message content and optional conversation ID for context.</param>
    /// <returns>Structured health assistant response with appointment data, symptom changes, and conversation ID.</returns>
    /// <exception cref="Exceptions.ApiValidationException">Thrown when input data is invalid (400).</exception>
    /// <exception cref="Exceptions.ApiUnauthorizedException">Thrown when user is not authenticated (401).</exception>
    /// <exception cref="Exceptions.ApiNotFoundException">Thrown when conversation is not found (404).</exception>
    /// <exception cref="Exceptions.ApiException">Thrown for other API errors.</exception>
    Task<HealthChatResponse> SendHealthMessageAsync(HealthChatRequest request);
}
