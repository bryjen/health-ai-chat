using Microsoft.AspNetCore.Components;

namespace WebFrontend.Services.Auth.OAuth.Providers;

/// <summary>
/// Microsoft/Azure AD OAuth provider implementation
/// </summary>
public class MicrosoftOAuthProvider : IOAuthProvider
{
    private readonly IConfiguration _configuration;
    private readonly NavigationManager _navigationManager;

    public string Name => "Microsoft";
    public string DisplayName => "Sign in with Microsoft";

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_configuration["OAuth:Microsoft:ClientId"]);

    public MicrosoftOAuthProvider(IConfiguration configuration, NavigationManager navigationManager)
    {
        _configuration = configuration;
        _navigationManager = navigationManager;
    }

    public string BuildAuthorizationUrl(string redirectUri, string providerName, string? returnUrl = null)
    {
        var clientId = _configuration["OAuth:Microsoft:ClientId"] 
            ?? throw new InvalidOperationException("Microsoft ClientId is not configured");
        var tenantId = _configuration["OAuth:Microsoft:TenantId"] ?? "common";

        var nonce = Guid.NewGuid().ToString("N");
        
        // Encode provider name and returnUrl in state parameter (Microsoft requires clean redirect_uri)
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
            ["response_mode"] = "fragment",
            ["scope"] = "openid email profile",
            ["nonce"] = nonce,
            ["state"] = state
        };

        var queryString = string.Join("&",
            queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        return $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize?{queryString}";
    }

    public OAuthCallbackResult ExtractTokenFromCallback(Uri callbackUri)
    {
        // Microsoft also uses fragment by default
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
