using Microsoft.AspNetCore.Components;

namespace WebFrontend.Services.Auth.OAuth.Providers;

/// <summary>
/// GitHub OAuth provider implementation
/// </summary>
public class GitHubOAuthProvider : IOAuthProvider
{
    private readonly IConfiguration _configuration;
    private readonly NavigationManager _navigationManager;

    public string Name => "GitHub";
    public string DisplayName => "Sign in with GitHub";

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_configuration["OAuth:GitHub:ClientId"]);

    public GitHubOAuthProvider(IConfiguration configuration, NavigationManager navigationManager)
    {
        _configuration = configuration;
        _navigationManager = navigationManager;
    }

    public string BuildAuthorizationUrl(string redirectUri, string providerName, string? returnUrl = null)
    {
        var clientId = _configuration["OAuth:GitHub:ClientId"] 
            ?? throw new InvalidOperationException("GitHub ClientId is not configured");

        // Encode provider name and returnUrl in state parameter
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
            ["scope"] = "user:email",
            ["state"] = state
        };

        var queryString = string.Join("&",
            queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        return $"https://github.com/login/oauth/authorize?{queryString}";
    }

    public OAuthCallbackResult ExtractTokenFromCallback(Uri callbackUri)
    {
        // GitHub returns authorization code in query parameters (not fragment)
        var query = callbackUri.Query.TrimStart('?');
        var paramsDict = ParseQueryString(query);

        return new OAuthCallbackResult
        {
            AuthorizationCode = paramsDict.GetValueOrDefault("code"),
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
