using System.Net.Http.Json;
using System.Text.Json;
using Web.Common.DTOs;

namespace WebApi.ApiWrapper.Services;

/// <summary>
/// Base class for API clients providing common functionality for HTTP requests, error handling, and token management.
/// </summary>
public abstract class BaseApiClient
{
    /// <summary>
    /// Shared JSON serializer options configured to match the backend API (snake_case_lower).
    /// </summary>
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// The HTTP client for making API requests.
    /// </summary>
    protected readonly HttpClient HttpClient;
    
    /// <summary>
    /// Optional token provider for authentication.
    /// </summary>
    protected readonly ITokenProvider? TokenProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client for making requests.</param>
    /// <param name="tokenProvider">Optional token provider for authentication.</param>
    protected BaseApiClient(HttpClient httpClient, ITokenProvider? tokenProvider = null)
    {
        HttpClient = httpClient;
        TokenProvider = tokenProvider;
    }

    /// <summary>
    /// Sets the Authorization header with a Bearer token.
    /// </summary>
    /// <param name="token">The JWT token to use for authentication.</param>
    protected void SetAuthorizationHeader(string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            HttpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            HttpClient.DefaultRequestHeaders.Authorization = null;
        }
    }

    /// <summary>
    /// Ensures the Authorization header is set from the token provider if available.
    /// </summary>
    protected async Task EnsureAuthenticatedAsync()
    {
        if (TokenProvider != null)
        {
            var token = await TokenProvider.GetTokenAsync();
            SetAuthorizationHeader(token);
        }
    }

    /// <summary>
    /// Handles error responses from the API and throws appropriate exceptions.
    /// </summary>
    /// <param name="response">The HTTP response message.</param>
    /// <exception cref="Exceptions.ApiValidationException">Thrown for 400 Bad Request with validation errors.</exception>
    /// <exception cref="Exceptions.ApiUnauthorizedException">Thrown for 401 Unauthorized.</exception>
    /// <exception cref="Exceptions.ApiNotFoundException">Thrown for 404 Not Found.</exception>
    /// <exception cref="Exceptions.ApiConflictException">Thrown for 409 Conflict.</exception>
    /// <exception cref="Exceptions.ApiException">Thrown for other error status codes.</exception>
    protected async Task HandleErrorResponseAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        ErrorResponse? errorResponse = null;
        string? errorCode = null;

        // Try to deserialize error response
        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                errorResponse = JsonSerializer.Deserialize<ErrorResponse>(content, JsonOptions);
                errorCode = errorResponse?.ErrorCode;
            }
            catch
            {
                // If deserialization fails, use the raw content as the message
            }
        }

        var message = errorResponse?.Message ?? content ?? $"HTTP {(int)response.StatusCode} {response.StatusCode}";

        // Map status codes to specific exceptions
        switch ((int)response.StatusCode)
        {
            case 400:
                var errors = errorResponse?.Errors ?? new Dictionary<string, string[]>();
                throw new Exceptions.ApiValidationException(message, errors, errorCode);

            case 401:
                throw new Exceptions.ApiUnauthorizedException(message, errorCode);

            case 404:
                throw new Exceptions.ApiNotFoundException(message, errorCode);

            case 409:
                throw new Exceptions.ApiConflictException(message, errorCode);

            default:
                throw new Exceptions.ApiException(message, (int)response.StatusCode, errorCode);
        }
    }

    /// <summary>
    /// Sends an HTTP request and handles the response, including error handling.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the response to.</typeparam>
    /// <param name="request">The HTTP request message.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The deserialized response object.</returns>
    protected async Task<T?> SendRequestAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync();

        var response = await HttpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    /// <summary>
    /// Sends an HTTP request without expecting a response body.
    /// </summary>
    /// <param name="request">The HTTP request message.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    protected async Task SendRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        await EnsureAuthenticatedAsync();

        var response = await HttpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }
    }
}
