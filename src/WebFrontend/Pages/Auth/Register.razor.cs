using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using WebFrontend.Models.Auth;
using WebFrontend.Services.Auth;

namespace WebFrontend.Pages.Auth;

public partial class Register : ComponentBase
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

    private string InputClass => "flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background file:border-0 file:bg-transparent file:text-sm file:font-medium placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50";
    private bool _oauthCallbackHandled;

    private void TogglePassword() => ShowPassword = !ShowPassword;

    private async Task HandleSubmit()
    {
        ErrorMessage = string.Empty;
        IsLoading = true;

        if (string.IsNullOrWhiteSpace(Model.FirstName) || string.IsNullOrWhiteSpace(Model.LastName))
        {
            ErrorMessage = "First and last name are required.";
            IsLoading = false;
            return;
        }

        if (Model.Password != Model.ConfirmPassword)
        {
            ErrorMessage = "Passwords do not match.";
            IsLoading = false;
            return;
        }

        try
        {
            var success = await AuthService.RegisterAsync(Model.Email, Model.Password, Model.FirstName, Model.LastName);
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

        ErrorMessage = errorMessage ?? "OAuth sign-up failed";
    }

    private static string? GetQueryParam(Uri uri, string key)
    {
        if (string.IsNullOrWhiteSpace(uri.Query))
        {
            return null;
        }

        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in query)
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && string.Equals(kv[0], key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(kv[1]);
            }
        }

        return null;
    }
}
