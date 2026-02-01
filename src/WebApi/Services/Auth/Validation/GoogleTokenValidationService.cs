using Google.Apis.Auth;

namespace WebApi.Services.Auth.Validation;

/// <summary>
/// Service for validating Google ID tokens
/// </summary>
public class GoogleTokenValidationService 
    : ITokenValidationService
{
    /// <summary>
    /// Google doesn't use authorization code flow in this implementation, so this method throws NotSupportedException
    /// </summary>
    public Task<TokenValidationResult> ValidateAuthorizationCodeAsync(string code, string redirectUri, string expectedClientId, string? clientSecret)
    {
        throw new NotSupportedException("Google OAuth uses ID token flow, not authorization code flow");
    }

    /// <summary>
    /// Validates a Google ID token and returns the user information
    /// </summary>
    /// <param name="idToken">The Google ID token to validate</param>
    /// <param name="expectedClientId">The expected Google Client ID (audience)</param>
    /// <returns>The validated token result containing user ID and email</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if token validation fails</exception>
    public async Task<TokenValidationResult> ValidateIdTokenAsync(string idToken, string expectedClientId)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            throw new UnauthorizedAccessException("ID token is required");
        }

        if (string.IsNullOrWhiteSpace(expectedClientId))
        {
            throw new InvalidOperationException("Google Client ID is not configured");
        }

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { expectedClientId }
            };

            // This automatically fetches Google's public keys and validates the token
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);

            if (string.IsNullOrWhiteSpace(payload.Subject))
            {
                throw new UnauthorizedAccessException("Google ID token missing user ID");
            }

            if (string.IsNullOrWhiteSpace(payload.Email))
            {
                throw new UnauthorizedAccessException("Google ID token missing email");
            }

            return new TokenValidationResult(payload.Subject, payload.Email);
        }
        catch (InvalidJwtException ex)
        {
            throw new UnauthorizedAccessException("Invalid Google ID token", ex);
        }
        catch (Exception ex)
        {
            throw new UnauthorizedAccessException("Failed to validate Google ID token", ex);
        }
    }
}
