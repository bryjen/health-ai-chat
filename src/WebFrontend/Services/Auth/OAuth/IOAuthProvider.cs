namespace WebFrontend.Services.Auth.OAuth;

/// <summary>
/// Interface for OAuth provider implementations
/// </summary>
public interface IOAuthProvider
{
    /// <summary>
    /// Provider name (e.g., "Google", "Microsoft")
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Display name for UI (e.g., "Sign in with Google")
    /// </summary>
    string DisplayName { get; }
    
    /// <summary>
    /// Whether this provider is enabled (has configuration)
    /// </summary>
    bool IsEnabled { get; }
    
    /// <summary>
    /// Builds the OAuth authorization URL for this provider
    /// </summary>
    /// <param name="redirectUri">The callback URI where the provider should redirect after authentication (must be clean, no query params)</param>
    /// <param name="providerName">Name of the provider (to encode in state parameter)</param>
    /// <param name="returnUrl">Optional return URL to redirect to after successful authentication</param>
    /// <returns>The complete authorization URL</returns>
    string BuildAuthorizationUrl(string redirectUri, string providerName, string? returnUrl = null);
    
    /// <summary>
    /// Extracts the ID token and error information from the OAuth callback URL
    /// </summary>
    /// <param name="callbackUri">The callback URI from the OAuth provider</param>
    /// <returns>OAuth callback result containing token or error information</returns>
    OAuthCallbackResult ExtractTokenFromCallback(Uri callbackUri);
}

/// <summary>
/// Result of OAuth callback containing token or error information
/// </summary>
public record OAuthCallbackResult
{
    public string? IdToken { get; init; }
    public string? AuthorizationCode { get; init; }
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }
}
