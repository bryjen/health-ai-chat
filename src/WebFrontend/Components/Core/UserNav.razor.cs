using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using WebFrontend.Services;
using WebFrontend.Services.Auth;

namespace WebFrontend.Components.Core;

public partial class UserNav : ComponentBase
{
    [Inject]
    private AuthService AuthService { get; set; } = null!;
    [Inject]
    private NavigationManager Navigation { get; set; } = null!;
    [Inject]
    private AuthenticationStateProvider AuthStateProvider { get; set; } = null!;
    [Inject]
    private ChatHubClient ChatHubClient { get; set; } = null!;

    private string GetUsername(AuthenticationState authState)
    {
        return AuthService.CurrentUser?.Email ?? authState.User?.Identity?.Name ?? "User";
    }

    private string GetDisplayName(AuthenticationState authState)
    {
        return AuthService.CurrentUser?.Email?.Split('@')[0] ?? authState.User?.Identity?.Name ?? "User";
    }

    private string GetEmail(AuthenticationState authState)
    {
        return AuthService.CurrentUser?.Email ?? authState.User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "";
    }

    private string GetInitials(AuthenticationState authState)
    {
        var name = GetDisplayName(authState);
        if (string.IsNullOrWhiteSpace(name)) return "U";
        return name[0].ToString().ToUpper();
    }

    private async Task HandleLogout()
    {
        // Disconnect SignalR connection
        await ChatHubClient.DisconnectAsync();
        
        // Clear all authentication data
        await AuthService.LogoutAsync();
        
        // Notify authentication state change
        if (AuthStateProvider is AuthStateProvider provider)
        {
            provider.NotifyUserChanged();
        }
        
        // Navigate to login page
        Navigation.NavigateTo("/login", forceLoad: true);
    }
}
