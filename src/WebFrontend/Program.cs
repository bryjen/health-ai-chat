using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using WebApi.ApiWrapper.Services;
using WebFrontend;
using WebFrontend.Services;
using WebFrontend.Services.Auth;
using WebFrontend.Services.Auth.OAuth;
using WebFrontend.Services.Auth.OAuth.Providers;
using ILocationApiClient = WebApi.ApiWrapper.Services.ILocationApiClient;
using LocationApiClient = WebApi.ApiWrapper.Services.LocationApiClient;
using EpisodesApiClient = WebApi.ApiWrapper.Services.EpisodesApiClient;
using AssessmentsApiClient = WebApi.ApiWrapper.Services.AssessmentsApiClient;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Register HttpClient for general use
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Register TokenProvider first (before API wrapper)
builder.Services.AddScoped<ITokenProvider, LocalStorageTokenProvider>();
builder.Services.AddScoped<LocalStorageTokenProvider>();
builder.Services.AddScoped<TokenProviderHttpMessageHandler>();

// Register API wrapper with backend URL
var backendUrl = "https://localhost:7265/";
var baseUri = new Uri(backendUrl);
var jsonHeader = new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json");

// Register a separate HttpClient for refresh calls (without refresh handler to avoid circular dependency)
builder.Services.AddHttpClient("RefreshClient", client =>
{
    client.BaseAddress = baseUri;
    client.DefaultRequestHeaders.Accept.Add(jsonHeader);
});

// Register TokenRefreshHttpMessageHandler
builder.Services.AddScoped<TokenRefreshHttpMessageHandler>(sp =>
{
    var tokenProvider = sp.GetRequiredService<ITokenProvider>();
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var refreshClient = httpClientFactory.CreateClient("RefreshClient");
    var localStorageTokenProvider = sp.GetRequiredService<LocalStorageTokenProvider>();
    var authService = sp.GetService<AuthService>();
    var authStateProvider = sp.GetService<AuthenticationStateProvider>();
    return new TokenRefreshHttpMessageHandler(tokenProvider, refreshClient, localStorageTokenProvider, authService, authStateProvider);
});

// Register API clients manually to have full control
// AuthApiClient needs token provider for GetCurrentUserAsync, but token is optional for login/register
// Note: AuthApiClient doesn't use TokenRefreshHttpMessageHandler to avoid circular dependency on refresh endpoint
builder.Services.AddHttpClient<IAuthApiClient>((sp, client) =>
{
    client.BaseAddress = baseUri;
    client.DefaultRequestHeaders.Accept.Add(jsonHeader);
})
.AddHttpMessageHandler<TokenProviderHttpMessageHandler>()
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler())
.AddTypedClient<IAuthApiClient>((httpClient, sp) =>
{
    var tokenProvider = sp.GetRequiredService<ITokenProvider>();
    return new AuthApiClient(httpClient, tokenProvider);
});

// Authenticated clients need token refresh handler (outermost) and token provider handler (innermost)
builder.Services.AddHttpClient<IConversationsApiClient>((sp, client) =>
{
    client.BaseAddress = baseUri;
    client.DefaultRequestHeaders.Accept.Add(jsonHeader);
})
.AddHttpMessageHandler<TokenRefreshHttpMessageHandler>()
.AddHttpMessageHandler<TokenProviderHttpMessageHandler>()
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler())
.AddTypedClient<IConversationsApiClient>((httpClient, sp) =>
{
    var tokenProvider = sp.GetRequiredService<ITokenProvider>();
    return new ConversationsApiClient(httpClient, tokenProvider);
});

builder.Services.AddHttpClient<IHealthChatApiClient>((sp, client) =>
{
    client.BaseAddress = baseUri;
    client.DefaultRequestHeaders.Accept.Add(jsonHeader);
})
.AddHttpMessageHandler<TokenRefreshHttpMessageHandler>()
.AddHttpMessageHandler<TokenProviderHttpMessageHandler>()
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler())
.AddTypedClient<IHealthChatApiClient>((httpClient, sp) =>
{
    var tokenProvider = sp.GetRequiredService<ITokenProvider>();
    return new HealthChatApiClient(httpClient, tokenProvider);
});

builder.Services.AddHttpClient<IEpisodesApiClient>((sp, client) =>
{
    client.BaseAddress = baseUri;
    client.DefaultRequestHeaders.Accept.Add(jsonHeader);
})
.AddHttpMessageHandler<TokenRefreshHttpMessageHandler>()
.AddHttpMessageHandler<TokenProviderHttpMessageHandler>()
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler())
.AddTypedClient<IEpisodesApiClient>((httpClient, sp) =>
{
    var tokenProvider = sp.GetRequiredService<ITokenProvider>();
    return new EpisodesApiClient(httpClient, tokenProvider);
});

builder.Services.AddHttpClient<IAssessmentsApiClient>((sp, client) =>
{
    client.BaseAddress = baseUri;
    client.DefaultRequestHeaders.Accept.Add(jsonHeader);
})
.AddHttpMessageHandler<TokenRefreshHttpMessageHandler>()
.AddHttpMessageHandler<TokenProviderHttpMessageHandler>()
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler())
.AddTypedClient<IAssessmentsApiClient>((httpClient, sp) =>
{
    var tokenProvider = sp.GetRequiredService<ITokenProvider>();
    return new AssessmentsApiClient(httpClient, tokenProvider);
});

// Register location API client (public endpoints, no auth required)
builder.Services.AddHttpClient<ILocationApiClient>((sp, client) =>
{
    client.BaseAddress = baseUri;
    client.DefaultRequestHeaders.Accept.Add(jsonHeader);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler())
.AddTypedClient<ILocationApiClient>((httpClient, sp) =>
{
    return new LocationApiClient(httpClient);
});

// Register authorization and OAuth services
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuthenticationStateProvider, AuthStateProvider>();
builder.Services.AddScoped<OAuthService>();
builder.Services.AddScoped<OAuthProviderRegistry>();
builder.Services.AddScoped<IOAuthProvider, GoogleOAuthProvider>();
builder.Services.AddScoped<IOAuthProvider, MicrosoftOAuthProvider>();
builder.Services.AddScoped<IOAuthProvider, GitHubOAuthProvider>();
builder.Services.AddScoped<ChatHubClient>();

// Register dropdown service
builder.Services.AddScoped<DropdownService>();

// Register dialog service as scoped
builder.Services.AddScoped<DialogService>();

// Register scroll lock service (ref-count for Dialog, Dropdown, etc.)
builder.Services.AddScoped<ScrollLockService>();

// Register toast service as singleton so it persists across components
builder.Services.AddSingleton<ToastService>();

// Register location service for country/state/city selector
builder.Services.AddScoped<LocationService>();

await builder.Build().RunAsync();
