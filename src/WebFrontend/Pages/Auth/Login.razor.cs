using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using WebFrontend.Models.Auth;
using WebFrontend.Services.Auth;

namespace WebFrontend.Pages.Auth;

public partial class Login : ComponentBase
{
    [Inject]
    public AuthService AuthService { get; set; } = null!;
    [Inject]
    public AuthenticationStateProvider AuthStateProvider { get; set; } = null!;
    [Inject]
    public NavigationManager Navigation { get; set; } = null!;
    [Inject]
    public OAuthService OAuthService { get; set; } = null!;

    private AuthModel Model { get; set; } = new();
    private bool ShowPassword { get; set; }
    private bool IsLoading { get; set; }
    private string ErrorMessage { get; set; } = string.Empty;

    private bool _oauthCallbackHandled;

    private void TogglePassword() => ShowPassword = !ShowPassword;

    private async Task HandleSubmit()
    {
        ErrorMessage = string.Empty;
        IsLoading = true;

        try
        {
            var success = await AuthService.LoginAsync(Model.Email, Model.Password);
            if (success)
            {
                if (AuthStateProvider is AuthStateProvider provider)
                {
                    provider.NotifyUserChanged();
                }

                Navigation.NavigateTo("/chat");
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void HandleSocialLogin(string provider)
    {
        OAuthService.InitiateOAuthFlow(provider);
    }

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        if (_oauthCallbackHandled)
        {
            return;
        }

        var uri = new Uri(Navigation.Uri);

        // Check if this is an OAuth callback by looking for state parameter in fragment or query
        var fragment = uri.Fragment.TrimStart('#');
        var query = uri.Query.TrimStart('?');
        var hasState = fragment.Contains("state=", StringComparison.OrdinalIgnoreCase) ||
                       query.Contains("state=", StringComparison.OrdinalIgnoreCase) ||
                       fragment.Contains("code=", StringComparison.OrdinalIgnoreCase) ||
                       query.Contains("code=", StringComparison.OrdinalIgnoreCase);

        if (!hasState)
        {
            return;
        }

        _oauthCallbackHandled = true;
        var (providerName, success, errorMessage, returnUrl) = await OAuthService.HandleOAuthCallback(uri);

        if (success && !string.IsNullOrWhiteSpace(providerName))
        {
            // Notify authentication state change
            if (AuthStateProvider is AuthStateProvider provider)
            {
                provider.NotifyUserChanged();
            }

            // ReturnUrl is extracted from state by OAuthService
            Navigation.NavigateTo(returnUrl ?? "/chat");
            return;
        }

        ErrorMessage = errorMessage ?? "OAuth login failed";
    }

}

