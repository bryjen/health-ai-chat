using System.Net.Http.Json;
using System.Text.Json;
using Web.Common.DTOs.Auth;

namespace WebApi.ApiWrapper.Services;

/// <summary>
/// Implementation of <see cref="IAuthApiClient"/> for authentication API endpoints.
/// </summary>
public class AuthApiClient : BaseApiClient, IAuthApiClient
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuthApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client for making requests.</param>
    /// <param name="tokenProvider">Optional token provider for authentication.</param>
    public AuthApiClient(HttpClient httpClient, ITokenProvider? tokenProvider = null)
        : base(httpClient, tokenProvider)
    {
    }

    /// <inheritdoc/>
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var response = await HttpClient.PostAsJsonAsync("api/v1/auth/register", request, BaseApiClient.JsonOptions);
        
        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<AuthResponse>(BaseApiClient.JsonOptions);
        return result ?? throw new Exceptions.ApiException("Failed to deserialize registration response", 500);
    }

    /// <inheritdoc/>
    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var response = await HttpClient.PostAsJsonAsync("api/v1/auth/login", request, BaseApiClient.JsonOptions);
        
        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<AuthResponse>(BaseApiClient.JsonOptions);
        return result ?? throw new Exceptions.ApiException("Failed to deserialize login response", 500);
    }

    /// <inheritdoc/>
    public async Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request)
    {
        var response = await HttpClient.PostAsJsonAsync("api/v1/auth/refresh", request, BaseApiClient.JsonOptions);
        
        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<AuthResponse>(BaseApiClient.JsonOptions);
        return result ?? throw new Exceptions.ApiException("Failed to deserialize refresh token response", 500);
    }

    /// <inheritdoc/>
    public async Task<UserDto> GetCurrentUserAsync()
    {
        await EnsureAuthenticatedAsync();
        
        var response = await HttpClient.GetAsync("api/v1/auth/me");
        
        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<UserDto>(BaseApiClient.JsonOptions);
        return result ?? throw new Exceptions.ApiException("Failed to deserialize user response", 500);
    }

    /// <summary>
    /// Gets the current user's profile information.
    /// </summary>
    /// <returns>The user's profile data</returns>
    /// <exception cref="Exceptions.ApiUnauthorizedException">Thrown when user is not authenticated (401).</exception>
    /// <exception cref="Exceptions.ApiException">Thrown for other API errors.</exception>
    public async Task<UserProfileDto> GetCurrentProfileAsync()
    {
        await EnsureAuthenticatedAsync();

        var response = await HttpClient.GetAsync("api/v1/users/me");

        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<UserProfileDto>(BaseApiClient.JsonOptions);
        return result ?? throw new Exceptions.ApiException("Failed to deserialize profile response", 500);
    }

    /// <summary>
    /// Updates the current user's profile information.
    /// </summary>
    /// <param name="request">The profile update request</param>
    /// <returns>The updated user profile</returns>
    /// <exception cref="Exceptions.ApiValidationException">Thrown for validation errors (400).</exception>
    /// <exception cref="Exceptions.ApiUnauthorizedException">Thrown when user is not authenticated (401).</exception>
    /// <exception cref="Exceptions.ApiException">Thrown for other API errors.</exception>
    public async Task<UserProfileDto> UpdateCurrentProfileAsync(UpdateUserProfileRequest request)
    {
        await EnsureAuthenticatedAsync();

        var response = await HttpClient.PutAsJsonAsync("api/v1/users/me", request, BaseApiClient.JsonOptions);

        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<UserProfileDto>(BaseApiClient.JsonOptions);
        return result ?? throw new Exceptions.ApiException("Failed to deserialize updated profile response", 500);
    }

    /// <inheritdoc/>
    public async Task RequestPasswordResetAsync(string email)
    {
        var request = new { Email = email };
        var response = await HttpClient.PostAsJsonAsync("api/v1/auth/password-reset/request", request, BaseApiClient.JsonOptions);
        
        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }
    }

    /// <inheritdoc/>
    public async Task ConfirmPasswordResetAsync(string token, string newPassword)
    {
        var request = new { Token = token, NewPassword = newPassword };
        var response = await HttpClient.PostAsJsonAsync("api/v1/auth/password-reset/confirm", request, BaseApiClient.JsonOptions);
        
        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }
    }

    /// <inheritdoc/>
    public async Task<AuthResponse> OAuthLoginAsync(OAuthLoginRequest request)
    {
        var response = await HttpClient.PostAsJsonAsync("api/v1/auth/oauth", request, BaseApiClient.JsonOptions);
        
        if (!response.IsSuccessStatusCode)
        {
            await HandleErrorResponseAsync(response);
        }

        var result = await response.Content.ReadFromJsonAsync<AuthResponse>(BaseApiClient.JsonOptions);
        return result ?? throw new Exceptions.ApiException("Failed to deserialize OAuth login response", 500);
    }
}
