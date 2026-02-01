# WebApi.ApiWrapper

A strongly-typed API client wrapper for the WebApi endpoints, designed for use in Blazor, MAUI, and other .NET client applications.

## Features

- **Strongly-typed API clients** for all endpoints (Auth, Conversations, HealthChat)
- **Typed exceptions** for different HTTP error scenarios
- **Automatic token management** via `ITokenProvider`
- **HttpClient factory pattern** for proper lifecycle management
- **Reuses DTOs** from `Web.Common` project

## Installation

Add a project reference to `WebApi.ApiWrapper` in your client application:

```xml
<ProjectReference Include="..\WebApi.ApiWrapper\WebApi.ApiWrapper.csproj" />
```

## Registration

### Blazor

```csharp
using WebApi.ApiWrapper.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Register API wrapper with base URL from configuration
builder.Services.AddApiWrapper(builder.Configuration["ApiBaseUrl"] ?? "https://api.example.com");
```

### MAUI

```csharp
using WebApi.ApiWrapper.Extensions;

var builder = MauiApp.CreateBuilder();

// Platform-specific base URLs
var baseUrl = DeviceInfo.Platform == DevicePlatform.Android
    ? "http://10.0.2.2:5001"  // Android emulator
    : "http://localhost:5001";  // iOS simulator / Windows

builder.Services.AddApiWrapper(baseUrl);
```

## Usage

### Authentication

```csharp
public class AuthService
{
    private readonly IAuthApiClient _authClient;
    private readonly ITokenProvider _tokenProvider;

    public AuthService(IAuthApiClient authClient, ITokenProvider tokenProvider)
    {
        _authClient = authClient;
        _tokenProvider = tokenProvider;
    }

    public async Task<bool> LoginAsync(string email, string password)
    {
        try
        {
            var response = await _authClient.LoginAsync(new LoginRequest
            {
                Email = email,
                Password = password
            });

            // Store the access token
            _tokenProvider.SetToken(response.AccessToken);
            
            return true;
        }
        catch (ApiUnauthorizedException)
        {
            // Invalid credentials
            return false;
        }
        catch (ApiException ex)
        {
            // Other API errors
            Console.WriteLine($"Login failed: {ex.Message}");
            return false;
        }
    }

    public void Logout()
    {
        _tokenProvider.ClearToken();
    }
}
```

### Making Authenticated Requests

Once you've logged in and stored the token, all authenticated endpoints will automatically include the token:

```csharp
public class ConversationsService
{
    private readonly IConversationsApiClient _conversationsClient;

    public ConversationsService(IConversationsApiClient conversationsClient)
    {
        _conversationsClient = conversationsClient;
    }

    public async Task<List<ConversationSummaryDto>> GetConversationsAsync()
    {
        try
        {
            return await _conversationsClient.GetAllConversationsAsync();
        }
        catch (ApiUnauthorizedException)
        {
            // Token expired or invalid - redirect to login
            throw;
        }
        catch (ApiException ex)
        {
            // Handle other errors
            Console.WriteLine($"Failed to get conversations: {ex.Message}");
            throw;
        }
    }

    public async Task<ConversationDto?> GetConversationAsync(Guid id)
    {
        try
        {
            return await _conversationsClient.GetConversationByIdAsync(id);
        }
        catch (ApiNotFoundException)
        {
            // Conversation not found
            return null;
        }
    }
}
```

### Health Chat

```csharp
public class HealthChatService
{
    private readonly IHealthChatApiClient _healthChatClient;

    public HealthChatService(IHealthChatApiClient healthChatClient)
    {
        _healthChatClient = healthChatClient;
    }

    public async Task<HealthChatResponse> SendMessageAsync(string message, Guid? conversationId = null)
    {
        try
        {
            return await _healthChatClient.SendHealthMessageAsync(new HealthChatRequest
            {
                Message = message,
                ConversationId = conversationId
            });
        }
        catch (ApiValidationException ex)
        {
            // Handle validation errors
            foreach (var error in ex.Errors)
            {
                Console.WriteLine($"{error.Key}: {string.Join(", ", error.Value)}");
            }
            throw;
        }
    }
}
```

## Exception Handling

The API wrapper throws typed exceptions for different error scenarios:

### ApiValidationException (400)
Thrown when the API returns validation errors.

```csharp
try
{
    await _authClient.RegisterAsync(request);
}
catch (ApiValidationException ex)
{
    // Access validation errors
    foreach (var error in ex.Errors)
    {
        Console.WriteLine($"{error.Key}: {string.Join(", ", error.Value)}");
    }
}
```

### ApiUnauthorizedException (401)
Thrown when authentication fails or token is invalid.

```csharp
try
{
    await _conversationsClient.GetAllConversationsAsync();
}
catch (ApiUnauthorizedException)
{
    // Redirect to login page
    NavigationManager.NavigateTo("/login");
}
```

### ApiNotFoundException (404)
Thrown when a resource is not found.

```csharp
try
{
    var conversation = await _conversationsClient.GetConversationByIdAsync(id);
}
catch (ApiNotFoundException)
{
    // Handle not found
    return null;
}
```

### ApiConflictException (409)
Thrown when there's a conflict (e.g., user already exists).

```csharp
try
{
    await _authClient.RegisterAsync(request);
}
catch (ApiConflictException ex)
{
    // User already exists
    Console.WriteLine(ex.Message);
}
```

### ApiException (Other status codes)
Thrown for other HTTP error status codes.

```csharp
try
{
    await _authClient.LoginAsync(request);
}
catch (ApiException ex)
{
    Console.WriteLine($"Status: {ex.StatusCode}, Error: {ex.Message}");
    if (ex.ErrorCode != null)
    {
        Console.WriteLine($"Error Code: {ex.ErrorCode}");
    }
}
```

## Available Clients

### IAuthApiClient

- `RegisterAsync(RegisterRequest)` - Register a new user
- `LoginAsync(LoginRequest)` - Authenticate user
- `RefreshTokenAsync(RefreshTokenRequest)` - Refresh access token
- `GetCurrentUserAsync()` - Get current user info
- `RequestPasswordResetAsync(string email)` - Request password reset
- `ConfirmPasswordResetAsync(string token, string newPassword)` - Confirm password reset
- `OAuthLoginAsync(OAuthLoginRequest)` - OAuth login (Google, Microsoft, GitHub)

### IConversationsApiClient

- `GetAllConversationsAsync()` - Get all conversations for user
- `GetConversationByIdAsync(Guid id)` - Get specific conversation
- `UpdateConversationTitleAsync(Guid id, UpdateConversationTitleRequest)` - Update conversation title
- `DeleteConversationAsync(Guid id)` - Delete conversation

### IHealthChatApiClient

- `SendHealthMessageAsync(HealthChatRequest)` - Send health chat message

## Token Management

The `ITokenProvider` interface provides simple token management:

```csharp
public interface ITokenProvider
{
    Task<string?> GetTokenAsync();
    void SetToken(string? token);
    void ClearToken();
}
```

The default `TokenProvider` implementation stores tokens in memory. For production applications, consider implementing a custom `ITokenProvider` that stores tokens securely (e.g., using secure storage in MAUI or browser storage in Blazor).

## Advanced Configuration

You can further configure the HttpClient after registration:

```csharp
builder.Services.AddApiWrapper(baseUrl)
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .AddPolicyHandler(GetRetryPolicy());

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
}
```

## Notes

- All DTOs are reused from the `Web.Common` project - no duplication
- The API wrapper uses the HttpClient factory pattern for proper lifecycle management
- Authentication tokens are automatically injected into requests for authenticated endpoints
- The DebugController endpoints are explicitly excluded from the wrapper
