using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Web.Common.DTOs.Auth;
using WebApi.ApiWrapper.Exceptions;
using WebApi.ApiWrapper.Services;
using WebFrontend.Services.Auth;

namespace WebFrontend.Services;

/// <summary>
/// HTTP message handler that automatically refreshes expired access tokens on 401 responses.
/// Implements token refresh with retry logic and prevents infinite refresh loops.
/// </summary>
public class TokenRefreshHttpMessageHandler : DelegatingHandler
{
    private readonly ITokenProvider _tokenProvider;
    private readonly HttpClient _refreshHttpClient;
    private readonly LocalStorageTokenProvider _localStorageTokenProvider;
    private readonly AuthService? _authService;
    private readonly AuthenticationStateProvider? _authStateProvider;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private Task<AuthResponse?>? _refreshTask;

    public TokenRefreshHttpMessageHandler(
        ITokenProvider tokenProvider,
        HttpClient refreshHttpClient,
        LocalStorageTokenProvider localStorageTokenProvider,
        AuthService? authService = null,
        AuthenticationStateProvider? authStateProvider = null)
    {
        _tokenProvider = tokenProvider;
        _refreshHttpClient = refreshHttpClient;
        _localStorageTokenProvider = localStorageTokenProvider;
        _authService = authService;
        _authStateProvider = authStateProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // TokenProviderHttpMessageHandler (in the chain) will add the token to the request
        // Send the request
        var response = await base.SendAsync(request, cancellationToken);

        // If we get a 401, try to refresh the token
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // Don't attempt refresh if this is already a refresh request (avoid infinite loop)
            if (IsRefreshEndpoint(request.RequestUri))
            {
                return response;
            }

            // Attempt to refresh the token
            var refreshResponse = await RefreshTokenAsync(cancellationToken);
            
            if (refreshResponse != null)
            {
                // Retry the original request with the new token
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshResponse.AccessToken);
                
                // Dispose the original response before retrying
                response.Dispose();
                
                return await base.SendAsync(request, cancellationToken);
            }
            else
            {
                // Refresh failed - user will need to log in again
                // The response is already 401, which will trigger logout in the app
                return response;
            }
        }

        return response;
    }

    /// <summary>
    /// Refreshes the access token using the stored refresh token.
    /// Uses a semaphore to prevent concurrent refresh attempts.
    /// </summary>
    private async Task<AuthResponse?> RefreshTokenAsync(CancellationToken cancellationToken)
    {
        // Check if refresh is already in progress
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // If refresh is already in progress, wait for it
            if (_refreshTask != null)
            {
                return await _refreshTask;
            }

            // Start new refresh task
            _refreshTask = RefreshTokenInternalAsync(cancellationToken);
            var result = await _refreshTask;
            
            // Clear the task when done
            _refreshTask = null;
            
            return result;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Internal method that performs the actual token refresh.
    /// Uses a separate HttpClient to avoid circular dependency.
    /// </summary>
    private async Task<AuthResponse?> RefreshTokenInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Get refresh token
            var refreshToken = await _localStorageTokenProvider.GetRefreshTokenAsync();
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return null;
            }

            // Call refresh endpoint directly using HttpClient (not through AuthApiClient to avoid circular dependency)
            var refreshRequest = new RefreshTokenRequest
            {
                RefreshToken = refreshToken
            };

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                PropertyNameCaseInsensitive = true
            };

            var response = await _refreshHttpClient.PostAsJsonAsync("api/v1/auth/refresh", refreshRequest, jsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Refresh token is invalid or expired - clear tokens and notify auth state
                await ClearTokensAndNotifyAsync();
                return null;
            }

            var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>(jsonOptions, cancellationToken);
            if (authResponse == null)
            {
                return null;
            }

            // Update stored tokens
            _localStorageTokenProvider.SetToken(authResponse.AccessToken);
            await _localStorageTokenProvider.SetRefreshTokenAsync(authResponse.RefreshToken);

            return authResponse;
        }
        catch (Exception)
        {
            // Network error or other issue - don't clear tokens, might be temporary
            return null;
        }
    }

    /// <summary>
    /// Clears tokens and notifies authentication state provider of the change.
    /// </summary>
    private async Task ClearTokensAndNotifyAsync()
    {
        // Clear tokens via AuthService if available (to ensure consistency)
        if (_authService != null)
        {
            await _authService.ClearTokensAsync();
        }
        else
        {
            // Fallback: clear tokens directly
            await _localStorageTokenProvider.ClearTokenAsync();
            await _localStorageTokenProvider.SetRefreshTokenAsync(null);
        }

        // Notify authentication state provider to trigger UI update
        if (_authStateProvider is AuthStateProvider provider)
        {
            provider.NotifyUserChanged();
        }
    }

    /// <summary>
    /// Checks if the request URI is the refresh endpoint to avoid infinite refresh loops.
    /// </summary>
    private static bool IsRefreshEndpoint(Uri? requestUri)
    {
        if (requestUri == null)
            return false;

        var path = requestUri.AbsolutePath.ToLowerInvariant();
        return path.Contains("/auth/refresh", StringComparison.OrdinalIgnoreCase);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshLock?.Dispose();
        }
        base.Dispose(disposing);
    }
}
