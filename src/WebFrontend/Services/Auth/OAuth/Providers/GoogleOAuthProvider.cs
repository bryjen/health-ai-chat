using Microsoft.AspNetCore.Components;

namespace WebFrontend.Services.Auth.OAuth.Providers;

/// <summary>
/// Google OAuth provider implementation
/// </summary>
public class GoogleOAuthProvider : IOAuthProvider
{
    private readonly IConfiguration _configuration;
    private readonly NavigationManager _navigationManager;

    public string Name => "Google";
    public string DisplayName => "Sign in with Google";

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_configuration["OAuth:Google:ClientId"]);

    public GoogleOAuthProvider(IConfiguration configuration, NavigationManager navigationManager)
    {
        _configuration = configuration;
        _navigationManager = navigationManager;
    }

    public string BuildAuthorizationUrl(string redirectUri, string providerName, string? returnUrl = null)
    {
        var clientId = _configuration["OAuth:Google:ClientId"] 
            ?? throw new InvalidOperationException("Google ClientId is not configured");

        var nonce = Guid.NewGuid().ToString("N");
        
        // Encode provider name and returnUrl in state parameter (Google requires clean redirect_uri)
        var stateParts = new List<string> { $"provider:{providerName}" };
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            stateParts.Add($"returnUrl:{returnUrl}");
        }
        var state = string.Join("|", stateParts);

        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "id_token",
            ["scope"] = "openid email profile",
            ["nonce"] = nonce,
            ["state"] = state
        };

        var queryString = string.Join("&",
            queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        return $"https://accounts.google.com/o/oauth2/v2/auth?{queryString}";
    }

    public OAuthCallbackResult ExtractTokenFromCallback(Uri callbackUri)
    {
        // Google returns token in fragment: #id_token=...&state=...
        // Also check query params as fallback
        var fragment = callbackUri.Fragment.TrimStart('#');
        var query = callbackUri.Query.TrimStart('?');

        var paramsDict = ParseQueryString(fragment);
        if (paramsDict.Count == 0)
        {
            paramsDict = ParseQueryString(query);
        }

        return new OAuthCallbackResult
        {
            IdToken = paramsDict.GetValueOrDefault("id_token"),
            Error = paramsDict.GetValueOrDefault("error"),
            ErrorDescription = paramsDict.GetValueOrDefault("error_description")
        };
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
}
