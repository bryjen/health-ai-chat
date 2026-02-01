using WebApi.Models;
using WebApi.Services.Auth.Validation;

namespace WebApi.Services.Auth;

/// <summary>
/// Factory for getting the appropriate token validation service based on OAuth provider
/// </summary>
public class TokenValidationServiceFactory(
    GoogleTokenValidationService googleValidator,
    MicrosoftTokenValidationService microsoftValidator,
    GitHubTokenValidationService githubValidator)
{
    private readonly Dictionary<AuthProvider, ITokenValidationService> _validators = new()
    {
        { AuthProvider.Google, googleValidator },
        { AuthProvider.Microsoft, microsoftValidator },
        { AuthProvider.GitHub, githubValidator }
    };

    /// <summary>
    /// Gets the token validation service for the specified provider
    /// </summary>
    /// <param name="provider">The OAuth provider</param>
    /// <returns>The token validation service for the provider</returns>
    /// <exception cref="NotSupportedException">Thrown if provider is not supported</exception>
    public ITokenValidationService GetValidator(AuthProvider provider)
    {
        if (!_validators.TryGetValue(provider, out var validator))
        {
            throw new NotSupportedException($"OAuth provider '{provider}' is not supported");
        }

        return validator;
    }
}
