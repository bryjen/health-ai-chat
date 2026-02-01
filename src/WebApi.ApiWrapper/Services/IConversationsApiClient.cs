using Web.Common.DTOs.Conversations;

namespace WebApi.ApiWrapper.Services;

/// <summary>
/// Client interface for conversations API endpoints.
/// </summary>
public interface IConversationsApiClient
{
    /// <summary>
    /// Gets all conversations for the authenticated user.
    /// </summary>
    /// <returns>List of conversation summaries.</returns>
    /// <exception cref="Exceptions.ApiUnauthorizedException">Thrown when user is not authenticated (401).</exception>
    /// <exception cref="Exceptions.ApiException">Thrown for other API errors.</exception>
    Task<List<ConversationSummaryDto>> GetAllConversationsAsync();

    /// <summary>
    /// Gets a specific conversation by ID with all messages.
    /// </summary>
    /// <param name="id">The unique identifier of the conversation.</param>
    /// <returns>The requested conversation, or null if not found.</returns>
    /// <exception cref="Exceptions.ApiUnauthorizedException">Thrown when user is not authenticated (401).</exception>
    /// <exception cref="Exceptions.ApiNotFoundException">Thrown when conversation is not found (404).</exception>
    /// <exception cref="Exceptions.ApiException">Thrown for other API errors.</exception>
    Task<ConversationDto?> GetConversationByIdAsync(Guid id);

    /// <summary>
    /// Updates the title of a conversation.
    /// </summary>
    /// <param name="id">The unique identifier of the conversation.</param>
    /// <param name="request">The new title for the conversation.</param>
    /// <returns>The updated conversation.</returns>
    /// <exception cref="Exceptions.ApiValidationException">Thrown when title is invalid (400).</exception>
    /// <exception cref="Exceptions.ApiUnauthorizedException">Thrown when user is not authenticated (401).</exception>
    /// <exception cref="Exceptions.ApiNotFoundException">Thrown when conversation is not found (404).</exception>
    /// <exception cref="Exceptions.ApiException">Thrown for other API errors.</exception>
    Task<ConversationDto> UpdateConversationTitleAsync(Guid id, UpdateConversationTitleRequest request);

    /// <summary>
    /// Deletes a conversation.
    /// </summary>
    /// <param name="id">The unique identifier of the conversation.</param>
    /// <exception cref="Exceptions.ApiUnauthorizedException">Thrown when user is not authenticated (401).</exception>
    /// <exception cref="Exceptions.ApiNotFoundException">Thrown when conversation is not found (404).</exception>
    /// <exception cref="Exceptions.ApiException">Thrown for other API errors.</exception>
    Task DeleteConversationAsync(Guid id);
}
