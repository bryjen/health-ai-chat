namespace WebApi.Services.Auth.Validation;

/// <summary>
/// Interface for validating OAuth tokens from different providers
/// </summary>
public interface ITokenValidationService
{
    /// <summary>
    /// Validates an ID token and extracts user information
    /// </summary>
    /// <param name="idToken">The ID token to validate</param>
    /// <param name="expectedClientId">The expected client ID (audience) for validation</param>
    /// <returns>Token validation result containing user ID and email</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if token validation fails</exception>
    Task<TokenValidationResult> ValidateIdTokenAsync(string idToken, string expectedClientId);

    /// <summary>
    /// Validates an authorization code and extracts user information
    /// </summary>
    /// <param name="code">The authorization code to exchange for an access token</param>
    /// <param name="redirectUri">The redirect URI used in the authorization request</param>
    /// <param name="expectedClientId">The expected client ID</param>
    /// <param name="clientSecret">The client secret for token exchange</param>
    /// <returns>Token validation result containing user ID and email</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if code exchange or validation fails</exception>
    Task<TokenValidationResult> ValidateAuthorizationCodeAsync(string code, string redirectUri, string expectedClientId, string? clientSecret);
}

/// <summary>
/// Result of token validation containing extracted user information
/// </summary>
public record TokenValidationResult(string UserId, string Email);
