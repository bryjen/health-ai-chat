using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using WebApi.ApiWrapper.Services;

namespace WebApi.ApiWrapper.Extensions;

/// <summary>
/// Extension methods for registering API wrapper services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds API wrapper services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">The base URL of the API.</param>
    /// <returns>The HTTP client builder for further configuration.</returns>
    public static IHttpClientBuilder AddApiWrapper(this IServiceCollection services, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Base URL cannot be null or empty.", nameof(baseUrl));
        }

        // Ensure base URL ends with a slash for proper path concatenation
        if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
        {
            baseUrl += "/";
        }

        var baseUri = new Uri(baseUrl);

        // Register token provider as scoped
        services.AddScoped<ITokenProvider, TokenProvider>();

        // Configure HttpClient for AuthApiClient (no token needed for auth endpoints)
        var authBuilder = services.AddHttpClient<IAuthApiClient, AuthApiClient>((sp, client) =>
        {
            client.BaseAddress = baseUri;
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        });

        // Configure HttpClient for ConversationsApiClient (with token provider)
        services.AddHttpClient<IConversationsApiClient, ConversationsApiClient>((sp, client) =>
        {
            client.BaseAddress = baseUri;
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        })
        .ConfigureHttpClient((sp, client) =>
        {
            // Token will be injected via EnsureAuthenticatedAsync in BaseApiClient
        });

        // Configure HttpClient for HealthChatApiClient (with token provider)
        services.AddHttpClient<IHealthChatApiClient, HealthChatApiClient>((sp, client) =>
        {
            client.BaseAddress = baseUri;
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        })
        .ConfigureHttpClient((sp, client) =>
        {
            // Token will be injected via EnsureAuthenticatedAsync in BaseApiClient
        });

        // Return the auth builder for further configuration
        return authBuilder;
    }
}

