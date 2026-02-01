using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebApi.Services.Auth.Validation;

/// <summary>
/// Service for validating GitHub OAuth authorization codes and fetching user information
/// </summary>
public class GitHubTokenValidationService(
    IHttpClientFactory httpClientFactory,
    ILogger<GitHubTokenValidationService> logger)
    : ITokenValidationService
{
    /// <summary>
    /// GitHub doesn't use ID tokens, so this method throws NotSupportedException
    /// </summary>
    public Task<TokenValidationResult> ValidateIdTokenAsync(string idToken, string expectedClientId)
    {
        throw new NotSupportedException("GitHub OAuth uses authorization code flow, not ID tokens");
    }

    /// <summary>
    /// Exchanges GitHub authorization code for access token and fetches user information
    /// </summary>
    public async Task<TokenValidationResult> ValidateAuthorizationCodeAsync(string code, string redirectUri, string expectedClientId, string? clientSecret)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new UnauthorizedAccessException("Authorization code is required");
        }

        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            throw new InvalidOperationException("Redirect URI is required");
        }

        if (string.IsNullOrWhiteSpace(expectedClientId))
        {
            throw new InvalidOperationException("GitHub Client ID is not configured");
        }

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException("GitHub Client Secret is not configured");
        }

        try
        {
            // Step 1: Exchange authorization code for access token
            var accessToken = await ExchangeCodeForTokenAsync(code, redirectUri, expectedClientId, clientSecret);

            // Step 2: Fetch user information using access token
            var userInfo = await GetUserInfoAsync(accessToken);

            // Step 3: Get user email (may need to fetch from emails endpoint if private)
            var email = await GetUserEmailAsync(accessToken, userInfo);

            if (userInfo.Id == 0)
            {
                throw new UnauthorizedAccessException("GitHub user info missing user ID");
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                throw new UnauthorizedAccessException("GitHub user info missing email");
            }

            return new TokenValidationResult(userInfo.Id.ToString(), email);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "GitHub API request failed");
            throw new UnauthorizedAccessException("Failed to communicate with GitHub API", ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to validate GitHub authorization code");
            throw new UnauthorizedAccessException("Failed to validate GitHub authorization code", ex);
        }
    }

    private async Task<string> ExchangeCodeForTokenAsync(string code, string redirectUri, string clientId, string clientSecret)
    {
        var httpClient = httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        // GitHub expects form-encoded data, not JSON
        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", redirectUri)
        });

        var response = await httpClient.PostAsync("https://github.com/login/oauth/access_token", formData);
        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("GitHub token exchange failed: {StatusCode} - {Content}", response.StatusCode, responseContent);
            throw new UnauthorizedAccessException($"GitHub token exchange failed: {response.StatusCode}");
        }

        // GitHub returns JSON when Accept: application/json header is present
        // But it might also return error messages in JSON format even with 200 status
        GitHubTokenResponse? tokenResponse = null;
        try
        {
            // Use JsonSerializer directly to handle potential errors
            tokenResponse = JsonSerializer.Deserialize<GitHubTokenResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse GitHub response as JSON: {Content}", responseContent);
        }

        // Check if response contains an error (GitHub sometimes returns errors with 200 status)
        if (tokenResponse != null && !string.IsNullOrWhiteSpace(tokenResponse.Error))
        {
            logger.LogWarning("GitHub returned error: {Error} - {ErrorDescription}", tokenResponse.Error, tokenResponse.ErrorDescription);
            throw new UnauthorizedAccessException($"GitHub OAuth error: {tokenResponse.Error}");
        }

        // If JSON parsing succeeded and we have an access token
        if (tokenResponse != null && !string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            return tokenResponse.AccessToken;
        }

        // If JSON parsing failed, try parsing as form-encoded response (fallback)
        var formParams = responseContent.Split('&');
        foreach (var param in formParams)
        {
            var parts = param.Split('=', 2);
            if (parts.Length == 2)
            {
                var key = Uri.UnescapeDataString(parts[0]);
                var value = Uri.UnescapeDataString(parts[1]);
                
                if (key == "access_token" && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
                
                if (key == "error")
                {
                    logger.LogWarning("GitHub returned error in form response: {Error}", value);
                    throw new UnauthorizedAccessException($"GitHub OAuth error: {value}");
                }
            }
        }

        logger.LogWarning("GitHub token exchange returned invalid response: {Content}", responseContent);
        throw new UnauthorizedAccessException("GitHub token exchange returned invalid response");
    }

    private async Task<GitHubUserInfo> GetUserInfoAsync(string accessToken)
    {
        var httpClient = httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        httpClient.DefaultRequestHeaders.Add("User-Agent", "AspTemplate");

        var response = await httpClient.GetAsync("https://api.github.com/user");

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            logger.LogWarning("GitHub user info request failed: {StatusCode} - {Content}", response.StatusCode, errorContent);
            throw new UnauthorizedAccessException($"GitHub user info request failed: {response.StatusCode}");
        }

        var userInfo = await response.Content.ReadFromJsonAsync<GitHubUserInfo>();
        if (userInfo == null)
        {
            throw new UnauthorizedAccessException("GitHub user info response was null");
        }

        return userInfo;
    }

    private async Task<string> GetUserEmailAsync(string accessToken, GitHubUserInfo userInfo)
    {
        // If email is public, use it directly
        if (!string.IsNullOrWhiteSpace(userInfo.Email))
        {
            return userInfo.Email;
        }

        // Otherwise, fetch from emails endpoint
        var httpClient = httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        httpClient.DefaultRequestHeaders.Add("User-Agent", "AspTemplate");

        var response = await httpClient.GetAsync("https://api.github.com/user/emails");

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("GitHub emails request failed: {StatusCode}", response.StatusCode);
            throw new UnauthorizedAccessException("Failed to fetch GitHub user email");
        }

        var emails = await response.Content.ReadFromJsonAsync<List<GitHubEmail>>();
        if (emails == null || emails.Count == 0)
        {
            throw new UnauthorizedAccessException("No email found for GitHub user");
        }

        // Prefer primary email, otherwise use the first verified email
        var primaryEmail = emails.FirstOrDefault(e => e.Primary && e.Verified);
        if (primaryEmail != null)
        {
            return primaryEmail.Email;
        }

        var verifiedEmail = emails.FirstOrDefault(e => e.Verified);
        if (verifiedEmail != null)
        {
            return verifiedEmail.Email;
        }

        // Fallback to first email if none are verified
        return emails[0].Email;
    }

    private class GitHubTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
        
        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
        
        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
        
        [JsonPropertyName("error")]
        public string? Error { get; set; }
        
        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }
    }

    private class GitHubUserInfo
    {
        public long Id { get; set; }
        public string? Login { get; set; }
        public string? Email { get; set; }
        public string? Name { get; set; }
    }

    private class GitHubEmail
    {
        public string Email { get; set; } = string.Empty;
        public bool Primary { get; set; }
        public bool Verified { get; set; }
    }
}
