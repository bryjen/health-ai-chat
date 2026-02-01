namespace WebFrontend.Services.Auth.OAuth;

/// <summary>
/// Registry for managing OAuth providers
/// </summary>
public class OAuthProviderRegistry
{
    private readonly Dictionary<string, IOAuthProvider> _providers;

    public OAuthProviderRegistry(IEnumerable<IOAuthProvider> providers)
    {
        _providers = providers
            .Where(p => p.IsEnabled)
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets a provider by name
    /// </summary>
    /// <param name="name">Provider name (case-insensitive)</param>
    /// <returns>The provider if found and enabled, null otherwise</returns>
    public IOAuthProvider? GetProvider(string name)
    {
        _providers.TryGetValue(name, out var provider);
        return provider;
    }

    /// <summary>
    /// Gets all enabled providers
    /// </summary>
    /// <returns>Collection of enabled providers</returns>
    public IEnumerable<IOAuthProvider> GetAllEnabledProviders()
    {
        return _providers.Values;
    }
}
