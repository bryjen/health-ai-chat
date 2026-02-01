using Microsoft.AspNetCore.Components;

namespace WebFrontend.Layout;

public partial class AuthLayout : LayoutComponentBase
{
    [Inject]
    public NavigationManager Navigation { get; set; } = null!;

    private bool IsRegisterPage => Navigation.Uri.Contains("/register");

    private string ToggleHref => IsRegisterPage ? "/login" : "/register";

    private string ToggleText => IsRegisterPage ? "Sign in" : "Create account";
}
