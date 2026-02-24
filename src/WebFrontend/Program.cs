using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using ShadcnBlazor.Components.Dialog.Services;
using ShadcnBlazor.Components.Popover.Models;
using ShadcnBlazor.Components.Popover.Services;
using ShadcnBlazor.Components.Shared.Services;
using ShadcnBlazor.Components.Shared.Services.Interop;
using ShadcnBlazor.Components.Sheet.Services;
using TailwindMerge.Extensions;
using WebApi.ApiWrapper.Services;
using WebFrontend;
using WebFrontend.Components.Chat.Services;
using WebFrontend.Services;
using WebFrontend.Services.Auth;
using WebFrontend.Services.Auth.OAuth;
using WebFrontend.Services.Auth.OAuth.Providers;
using ILocationApiClient = WebApi.ApiWrapper.Services.ILocationApiClient;
using LocationApiClient = WebApi.ApiWrapper.Services.LocationApiClient;
using EpisodesApiClient = WebApi.ApiWrapper.Services.EpisodesApiClient;
using AssessmentsApiClient = WebApi.ApiWrapper.Services.AssessmentsApiClient;
using SymptomsApiClient = WebApi.ApiWrapper.Services.SymptomsApiClient;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddTailwindMerge();
builder.Services.AddAiChat();
builder.Services.AddOptions();
builder.Services.Configure<PopoverOptions>(_ => { });
builder.Services.AddScoped<IPopoverRegistry, PopoverRegistry>();
builder.Services.AddScoped<IPopoverService, PopoverService>();

builder.Services.AddScoped<IDialogService, DialogService>();
builder.Services.AddScoped<IDialogJsService, DialogJsService>();
builder.Services.AddScoped<ScrollLockService>();

builder.Services.AddScoped(sp => new PopoverInterop(
    sp.GetRequiredService<IJSRuntime>(),
    PopoverInterop.DefaultModulePaths));

builder.Services.AddScoped(sp => new FocusScopeInterop(
    sp.GetRequiredService<IJSRuntime>(),
    FocusScopeInterop.DefaultModulePaths));
builder.Services.AddScoped<IFocusScopeService, FocusScopeService>();

builder.Services.AddScoped(sp => new KeyInterceptorInterop(
    sp.GetRequiredService<IJSRuntime>(),
    KeyInterceptorInterop.DefaultModulePaths));
builder.Services.AddScoped<IKeyInterceptorService, KeyInterceptorService>();

builder.Services.AddScoped(sp => new ScrollLockInterop(
    sp.GetRequiredService<IJSRuntime>(),
    ScrollLockInterop.DefaultModulePaths));

builder.Services.AddScoped(sp => new DialogInterop(
    sp.GetRequiredService<IJSRuntime>(),
    DialogInterop.DefaultModulePaths));

builder.Services.AddScoped(sp => new SheetInterop(
    sp.GetRequiredService<IJSRuntime>(),
    SheetInterop.DefaultModulePaths));
builder.Services.AddScoped<ISheetJsService, SheetJsService>();
builder.Services.AddScoped<ISheetService, SheetService>();


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

builder.Services.AddSingleton(new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true
});

// Register TokenRefreshHttpMessageHandler
builder.Services.AddScoped<TokenRefreshHttpMessageHandler>(sp =>
{
    var tokenProvider = sp.GetRequiredService<ITokenProvider>();
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var refreshClient = httpClientFactory.CreateClient("RefreshClient");
    var localStorageTokenProvider = sp.GetRequiredService<LocalStorageTokenProvider>();
    var jsonOptions = sp.GetRequiredService<JsonSerializerOptions>();
    var authService = sp.GetService<AuthService>();
    var authStateProvider = sp.GetService<AuthenticationStateProvider>();
    return new TokenRefreshHttpMessageHandler(tokenProvider, refreshClient, localStorageTokenProvider, jsonOptions, authService,
        authStateProvider);
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

builder.Services.AddHttpClient<ISymptomsApiClient>((sp, client) =>
    {
        client.BaseAddress = baseUri;
        client.DefaultRequestHeaders.Accept.Add(jsonHeader);
    })
    .AddHttpMessageHandler<TokenRefreshHttpMessageHandler>()
    .AddHttpMessageHandler<TokenProviderHttpMessageHandler>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler())
    .AddTypedClient<ISymptomsApiClient>((httpClient, sp) =>
    {
        var tokenProvider = sp.GetRequiredService<ITokenProvider>();
        return new SymptomsApiClient(httpClient, tokenProvider);
    });

// Register location API client (public endpoints, no auth required)
builder.Services.AddHttpClient<ILocationApiClient>((sp, client) =>
    {
        client.BaseAddress = baseUri;
        client.DefaultRequestHeaders.Accept.Add(jsonHeader);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler())
    .AddTypedClient<ILocationApiClient>((httpClient, sp) => { return new LocationApiClient(httpClient); });

// Register authorization and OAuth services
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuthenticationStateProvider, AuthStateProvider>();
builder.Services.AddScoped<OAuthService>();
builder.Services.AddScoped<OAuthProviderRegistry>();
builder.Services.AddScoped<IOAuthProvider, GoogleOAuthProvider>();
builder.Services.AddScoped<IOAuthProvider, MicrosoftOAuthProvider>();
builder.Services.AddScoped<IOAuthProvider, GitHubOAuthProvider>();
builder.Services.AddScoped<ChatHubClient>(sp =>
{
    var tokenProvider = sp.GetRequiredService<ITokenProvider>();
    var jsonOptions = sp.GetRequiredService<JsonSerializerOptions>();
    return new ChatHubClient(tokenProvider, jsonOptions);
});

// Register location service for country/state/city selector
builder.Services.AddScoped<LocationService>();

await builder.Build().RunAsync();
