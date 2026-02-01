using Microsoft.JSInterop;
using WebApi.ApiWrapper.Services;

namespace WebFrontend.Services;

/// <summary>
/// Token provider that stores authentication tokens in browser localStorage.
/// </summary>
public class LocalStorageTokenProvider : ITokenProvider
{
    private const string TokenKey = "authToken";
    private const string RefreshTokenKey = "refreshToken";
    private const string UserKey = "user";
    
    private readonly IJSRuntime _jsRuntime;
    private string? _cachedToken;

    public LocalStorageTokenProvider(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task<string?> GetTokenAsync()
    {
        if (_cachedToken != null)
            return _cachedToken;

        try
        {
            _cachedToken = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", TokenKey);
            return _cachedToken;
        }
        catch (JSException)
        {
            // localStorage might not be available (e.g., in SSR scenarios)
            return null;
        }
    }

    public void SetToken(string? token)
    {
        _cachedToken = token;
        
        if (token != null)
        {
            _ = _jsRuntime.InvokeVoidAsync("localStorage.setItem", TokenKey, token);
        }
        else
        {
            _ = _jsRuntime.InvokeVoidAsync("localStorage.removeItem", TokenKey);
        }
    }

    public async Task ClearTokenAsync()
    {
        _cachedToken = null;
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", TokenKey);
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", RefreshTokenKey);
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", UserKey);
    }
    
    public void ClearToken()
    {
        // Legacy synchronous method - use ClearTokenAsync instead
        _cachedToken = null;
        _ = _jsRuntime.InvokeVoidAsync("localStorage.removeItem", TokenKey);
        _ = _jsRuntime.InvokeVoidAsync("localStorage.removeItem", RefreshTokenKey);
        _ = _jsRuntime.InvokeVoidAsync("localStorage.removeItem", UserKey);
    }

    public async Task SetRefreshTokenAsync(string? refreshToken)
    {
        if (refreshToken != null)
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", RefreshTokenKey, refreshToken);
        }
        else
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", RefreshTokenKey);
        }
    }

    public async Task<string?> GetRefreshTokenAsync()
    {
        try
        {
            return await _jsRuntime.InvokeAsync<string>("localStorage.getItem", RefreshTokenKey);
        }
        catch (JSException)
        {
            return null;
        }
    }

    public async Task SetUserAsync(string? userJson)
    {
        if (userJson != null)
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", UserKey, userJson);
        }
        else
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", UserKey);
        }
    }

    public async Task<string?> GetUserAsync()
    {
        try
        {
            return await _jsRuntime.InvokeAsync<string>("localStorage.getItem", UserKey);
        }
        catch (JSException)
        {
            return null;
        }
    }
}
