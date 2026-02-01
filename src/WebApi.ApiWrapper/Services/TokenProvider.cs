namespace WebApi.ApiWrapper.Services;

/// <summary>
/// Simple in-memory implementation of <see cref="ITokenProvider"/>.
/// </summary>
public class TokenProvider : ITokenProvider
{
    private string? _token;

    /// <inheritdoc/>
    public Task<string?> GetTokenAsync()
    {
        return Task.FromResult(_token);
    }

    /// <inheritdoc/>
    public void SetToken(string? token)
    {
        _token = token;
    }

    /// <inheritdoc/>
    public void ClearToken()
    {
        _token = null;
    }
}
