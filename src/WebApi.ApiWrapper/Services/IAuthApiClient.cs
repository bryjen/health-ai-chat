using Web.Common.DTOs.Auth;

namespace WebApi.ApiWrapper.Services;

/// <summary>
/// Client interface for authentication API endpoints.
/// </summary>
public interface IAuthApiClient
{
    /// <summary>
    /// Registers a new user account.
    /// </summary>
    /// <param name="request">User registration information.</param>
    /// <returns>Authentication response with tokens.</returns>
    /// <exception cref="Exceptions.ApiValidationException">Thrown when validation fails (400).</exception>
    /// <exception cref="Exceptions.ApiConflictException">Thrown when user already exists (409).</exception>
    /// <exception cref="Exceptions.ApiException">Thrown for other API errors.</exception>
    Task<AuthResponse> RegisterAsync(RegisterRequest request);

    /// <summary>
    /// Authenticates an existing user.
    /// </summary>
    /// <param name="request">Login credentials.</param>
    /// <returns>Authentication response with tokens.</returns>
    /// <exception cref="Exceptions.ApiUnauthorizedException">Thrown when credentials are invalid (401).</exception>
    /// <exception cref="Exceptions.ApiException">Thrown for other API errors.</exception>
    Task<AuthResponse> LoginAsync(LoginRequest request);

    /// <summary>
    /// Refreshes an access token using a refresh token.
    /// </summary>
    /// <param name="request">Refresh token request.</param>
    /// <returns>New authentication response with tokens.</returns>
    /// <exception cref="Exceptions.ApiValidationException">Thrown when token is invalid or expired (400).</exception>
    /// <exception cref="Exceptions.ApiException">Thrown for other API errors.</exception>
    Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request);

    /// <summary>
    /// Gets the current authenticated user's information.
    /// </summary>
    /// <returns>Current user details.</returns>
    /// <exception cref="Exceptions.ApiUnauthorizedException">Thrown when user is not authenticated (401).</exception>
    /// <exception cref="Exceptions.ApiNotFoundException">Thrown when user is not found (404).</exception>
    /// <exception cref="Exceptions.ApiException">Thrown for other API errors.</exception>
    Task<UserDto> GetCurrentUserAsync();

    /// <summary>
    /// Gets the current authenticated user's profile, including personal details.
    /// </summary>
    Task<UserProfileDto> GetCurrentProfileAsync();

    /// <summary>
    /// Updates the current authenticated user's profile.
    /// </summary>
    Task<UserProfileDto> UpdateCurrentProfileAsync(UpdateUserProfileRequest request);

    /// <summary>
    /// Requests a password reset email.
    /// </summary>
    /// <param name="email">Email address for password reset.</param>
    /// <exception cref="Exceptions.ApiValidationException">Thrown when email is invalid (400).</exception>
    /// <exception cref="Exceptions.ApiException">Thrown for other API errors.</exception>
    Task RequestPasswordResetAsync(string email);

    /// <summary>
    /// Confirms password reset with token and new password.
    /// </summary>
    /// <param name="token">Password reset token from email.</param>
    /// <param name="newPassword">New password.</param>
    /// <exception cref="Exceptions.ApiValidationException">Thrown when token is invalid or password validation fails (400).</exception>
    /// <exception cref="Exceptions.ApiException">Thrown for other API errors.</exception>
    Task ConfirmPasswordResetAsync(string token, string newPassword);

    /// <summary>
    /// Authenticates with OAuth provider using ID token or authorization code.
    /// </summary>
    /// <param name="request">OAuth login request.</param>
    /// <returns>Authentication response with tokens.</returns>
    /// <exception cref="Exceptions.ApiValidationException">Thrown when request is invalid (400).</exception>
    /// <exception cref="Exceptions.ApiUnauthorizedException">Thrown when token validation fails (401).</exception>
    /// <exception cref="Exceptions.ApiConflictException">Thrown when account conflict occurs (409).</exception>
    /// <exception cref="Exceptions.ApiException">Thrown for other API errors.</exception>
    Task<AuthResponse> OAuthLoginAsync(OAuthLoginRequest request);
}
