using Microsoft.AspNetCore.Components;
using WebFrontend.Utils;

namespace WebFrontend.Components.UI.Avatar;

[ComponentMetadata(
    Description = "Displays a user avatar with image and fallback support.",
    IsEntry = true,
    Group = nameof(Avatar))]
public partial class Avatar
{
    public bool ShowFallback { get; set; } = false;
    private string? _imageSrc;

    public void OnImageError()
    {
        ShowFallback = true;
        StateHasChanged();
    }

    public string? GetImageSrc() => _imageSrc;

    public void SetImageSrc(string? src)
    {
        _imageSrc = src;
        ShowFallback = false;
    }
}
