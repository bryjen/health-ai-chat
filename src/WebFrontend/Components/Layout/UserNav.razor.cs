using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using WebFrontend.Services.Auth;

namespace WebFrontend.Components.Layout;

public partial class UserNav : ComponentBase
{
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private AuthService AuthService { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = null!;

    private async Task HandleLogout()
    {
        await AuthService.LogoutAsync();

        if (AuthStateProvider is AuthStateProvider provider)
            provider.NotifyUserChanged();

        Navigation.NavigateTo("/login", forceLoad: true);
    }

    private string GetDisplayName(AuthenticationState context)
    {
        var user = AuthService.CurrentUser;
        if (!string.IsNullOrEmpty(user?.Email))
            return user.Email;

        return context.User?.Identity?.Name ?? "User";
    }

    private string GetEmail(AuthenticationState context)
    {
        var user = AuthService.CurrentUser;
        if (!string.IsNullOrEmpty(user?.Email))
            return user.Email;

        return context.User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? string.Empty;
    }

    private string GetInitials(AuthenticationState context)
    {
        var name = GetDisplayName(context);
        if (string.IsNullOrWhiteSpace(name))
            return "?";

        var parts = name.Split(['@', ' '], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant()
            : name[..Math.Min(2, name.Length)].ToUpperInvariant();
    }
}
