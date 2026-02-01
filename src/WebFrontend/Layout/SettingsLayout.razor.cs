using Microsoft.AspNetCore.Components;

namespace WebFrontend.Layout;

public partial class SettingsLayout : LayoutComponentBase
{
    [Inject]
    public NavigationManager NavigationManager { get; set; } = null!;

    private string GetCurrentRelativeUri()
    {
        var baseBaseUri = NavigationManager.Uri[NavigationManager.BaseUri.Length..];
        return $"/{baseBaseUri}".Split("?")[0];
    }
}
