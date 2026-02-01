namespace WebApi.ApiWrapper.Services;

/// <summary>
/// Provides access to the current authentication token.
/// </summary>
public interface ITokenProvider
{
    /// <summary>
    /// Gets the current access token asynchronously.
    /// </summary>
    /// <returns>The access token, or null if not available.</returns>
    Task<string?> GetTokenAsync();

    /// <summary>
    /// Sets the access token.
    /// </summary>
    /// <param name="token">The access token to store.</param>
    void SetToken(string? token);

    /// <summary>
    /// Clears the stored access token.
    /// </summary>
    void ClearToken();
}
