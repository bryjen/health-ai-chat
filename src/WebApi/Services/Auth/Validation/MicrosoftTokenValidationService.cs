using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using WebApi.Configuration.Options;

namespace WebApi.Services.Auth.Validation;

/// <summary>
/// Service for validating Microsoft/Azure AD ID tokens
/// </summary>
public class MicrosoftTokenValidationService(
    IOptions<OAuthSettings> oauthSettings,
    ILogger<MicrosoftTokenValidationService> logger)
    : ITokenValidationService
{
    private readonly OAuthSettings _oauthSettings = oauthSettings.Value;

    /// <summary>
    /// Microsoft doesn't use authorization code flow in this implementation, so this method throws NotSupportedException
    /// </summary>
    public Task<TokenValidationResult> ValidateAuthorizationCodeAsync(string code, string redirectUri, string expectedClientId, string? clientSecret)
    {
        throw new NotSupportedException("Microsoft OAuth uses ID token flow, not authorization code flow");
    }

    /// <summary>
    /// Validates a Microsoft/Azure AD ID token and returns the user information
    /// </summary>
    /// <param name="idToken">The Microsoft ID token to validate</param>
    /// <param name="expectedClientId">The expected Microsoft Client ID (audience)</param>
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
            throw new InvalidOperationException("Microsoft Client ID is not configured");
        }

        try
        {
            var tenantId = _oauthSettings.Microsoft.TenantId;
            
            // First, decode the token to get the issuer (without full validation)
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(idToken))
            {
                throw new UnauthorizedAccessException("Invalid token format");
            }

            var token = handler.ReadJwtToken(idToken);
            var tokenIssuer = token.Issuer;

            // Determine the metadata endpoint based on the token's issuer
            string metadataAddress;
            if (tokenIssuer.Contains("9188040d-6c67-4c5b-b112-36a304b66dad"))
            {
                // Personal Microsoft account
                metadataAddress = "https://login.microsoftonline.com/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0/.well-known/openid-configuration";
            }
            else if (tokenIssuer.Contains("/common/"))
            {
                // Common endpoint
                metadataAddress = "https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration";
            }
            else
            {
                // Organizational account - extract tenant ID from issuer or use configured tenant
                metadataAddress = $"https://login.microsoftonline.com/{tenantId}/v2.0/.well-known/openid-configuration";
            }

            var configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                metadataAddress,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = true }
            );

            var config = await configurationManager.GetConfigurationAsync(CancellationToken.None);

            // Build valid issuers list
            var validIssuers = new List<string> { config.Issuer, tokenIssuer };
            
            if (tenantId == "common")
            {
                // Add common issuers for both personal and organizational accounts
                validIssuers.Add("https://login.microsoftonline.com/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0");
                validIssuers.Add("https://login.microsoftonline.com/common/v2.0");
            }

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = validIssuers.Distinct().ToArray(),
                ValidateAudience = true,
                ValidAudience = expectedClientId,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = config.SigningKeys,
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            var principal = handler.ValidateToken(idToken, validationParameters, out var validatedToken);

            // Extract user ID (sub or oid claim) and email
            var userId = principal.FindFirst("sub")?.Value 
                      ?? principal.FindFirst("oid")?.Value 
                      ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var email = principal.FindFirst("email")?.Value 
                     ?? principal.FindFirst("preferred_username")?.Value 
                     ?? principal.FindFirst(ClaimTypes.Email)?.Value;

            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new UnauthorizedAccessException("Microsoft ID token missing user ID");
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                throw new UnauthorizedAccessException("Microsoft ID token missing email");
            }

            return new TokenValidationResult(userId, email);
        }
        catch (SecurityTokenValidationException ex)
        {
            logger.LogWarning(ex, "Microsoft token validation failed: {Message}. Inner: {InnerMessage}", 
                ex.Message, ex.InnerException?.Message);
            throw new UnauthorizedAccessException($"Invalid Microsoft ID token: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to validate Microsoft ID token: {Message}", ex.Message);
            throw new UnauthorizedAccessException($"Failed to validate Microsoft ID token: {ex.Message}", ex);
        }
    }
}
