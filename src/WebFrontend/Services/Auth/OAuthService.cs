using Microsoft.AspNetCore.Components;
using System.Collections.Generic;
using Web.Common.DTOs.Auth;
using WebFrontend.Models;
using WebFrontend.Services.Auth.OAuth;

namespace WebFrontend.Services.Auth;

/// <summary>
/// Service for handling OAuth authentication flows
/// </summary>
public class OAuthService(
    OAuthProviderRegistry providerRegistry,
    AuthService authService,
    NavigationManager navigationManager)
{
    /// <summary>
    /// Initiates the OAuth flow for the specified provider
    /// </summary>
    /// <param name="providerName">Name of the OAuth provider (e.g., "Google", "Microsoft")</param>
    /// <param name="returnUrl">Optional URL to redirect to after successful authentication</param>
    public void InitiateOAuthFlow(string providerName, string? returnUrl = null)
    {
        var provider = providerRegistry.GetProvider(providerName);
        if (provider == null)
        {
            throw new InvalidOperationException($"OAuth provider '{providerName}' not found or not enabled");
        }

        // Use clean redirect URI without query parameters (required by Google OAuth)
        // Provider info will be encoded in the state parameter
        var redirectUri = $"{navigationManager.BaseUri}login";

        var authUrl = provider.BuildAuthorizationUrl(redirectUri, providerName, returnUrl);
        navigationManager.NavigateTo(authUrl, forceLoad: true);
    }

    /// <summary>
    /// Handles the OAuth callback and authenticates the user
    /// </summary>
    /// <param name="callbackUri">The callback URI from the OAuth provider</param>
    /// <returns>Tuple with provider name, success status, error message, and returnUrl</returns>
    public async Task<(string? ProviderName, bool Success, string? ErrorMessage, string? ReturnUrl)> HandleOAuthCallback(Uri callbackUri)
    {
        // Extract provider name from state parameter (encoded by provider)
        var result = ExtractOAuthResult(callbackUri);
        
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            var errorMessage = result.ErrorDescription ?? $"OAuth error: {result.Error}";
            return (null, false, errorMessage, null);
        }

        if (string.IsNullOrWhiteSpace(result.ProviderName))
        {
            return (null, false, "Provider name not found in OAuth callback", null);
        }

        var provider = providerRegistry.GetProvider(result.ProviderName);
        if (provider == null)
        {
            return (null, false, $"OAuth provider '{result.ProviderName}' not found or not enabled", null);
        }

        // Handle authorization code flow (GitHub)
        if (!string.IsNullOrWhiteSpace(result.AuthorizationCode))
        {
            // Reconstruct redirect URI without the code/state parameters (what was sent to GitHub)
            var baseUri = new Uri(callbackUri.GetLeftPart(UriPartial.Path));
            var redirectUriForBackend = baseUri.ToString();

            // Send authorization code to backend for validation and save session
            var loginResult = await authService.LoginWithOAuthAsync(result.ProviderName, authorizationCode: result.AuthorizationCode, redirectUri: redirectUriForBackend);
            return (result.ProviderName, loginResult.Success, loginResult.ErrorMessage, result.ReturnUrl);
        }
        // Handle ID token flow (Google, Microsoft)
        else if (!string.IsNullOrWhiteSpace(result.IdToken))
        {
            // Send ID token to backend for validation and save session
            var loginResult = await authService.LoginWithOAuthAsync(result.ProviderName, idToken: result.IdToken);
            return (result.ProviderName, loginResult.Success, loginResult.ErrorMessage, result.ReturnUrl);
        }
        else
        {
            return (result.ProviderName, false, "No authorization code or ID token received from OAuth provider", result.ReturnUrl);
        }
    }

    /// <summary>
    /// Extracts OAuth result from callback URI, including provider name from state
    /// </summary>
    private OAuthCallbackResultWithProvider ExtractOAuthResult(Uri callbackUri)
    {
        // Check fragment first (Google, Microsoft use fragment)
        var fragment = callbackUri.Fragment.TrimStart('#');
        var query = callbackUri.Query.TrimStart('?');

        var paramsDict = ParseQueryString(fragment);
        if (paramsDict.Count == 0)
        {
            paramsDict = ParseQueryString(query);
        }

        var state = paramsDict.GetValueOrDefault("state");
        var providerName = ExtractProviderFromState(state);

        return new OAuthCallbackResultWithProvider
        {
            ProviderName = providerName,
            IdToken = paramsDict.GetValueOrDefault("id_token"),
            AuthorizationCode = paramsDict.GetValueOrDefault("code"),
            Error = paramsDict.GetValueOrDefault("error"),
            ErrorDescription = paramsDict.GetValueOrDefault("error_description"),
            ReturnUrl = ExtractReturnUrlFromState(state)
        };
    }

    /// <summary>
    /// Extracts provider name and returnUrl from state parameter
    /// State format: "provider:Google|returnUrl:/chat" or just "provider:Google"
    /// </summary>
    private static string? ExtractProviderFromState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        var parts = state.Split('|');
        foreach (var part in parts)
        {
            if (part.StartsWith("provider:", StringComparison.OrdinalIgnoreCase))
            {
                return part.Substring("provider:".Length);
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts returnUrl from state parameter
    /// </summary>
    private static string? ExtractReturnUrlFromState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        var parts = state.Split('|');
        foreach (var part in parts)
        {
            if (part.StartsWith("returnUrl:", StringComparison.OrdinalIgnoreCase))
            {
                return part.Substring("returnUrl:".Length);
            }
        }

        return null;
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2)
            {
                result[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv[1]);
            }
        }
        return result;
    }

    private class OAuthCallbackResultWithProvider
    {
        public string? ProviderName { get; set; }
        public string? IdToken { get; set; }
        public string? AuthorizationCode { get; set; }
        public string? Error { get; set; }
        public string? ErrorDescription { get; set; }
        public string? ReturnUrl { get; set; }
    }

    private static string? GetQueryParam(Uri uri, string key)
    {
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in query)
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && string.Equals(kv[0], key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(kv[1]);
            }
        }
        return null;
    }
}
