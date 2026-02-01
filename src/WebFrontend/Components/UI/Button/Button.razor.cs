using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using WebFrontend.Components.UI.Shared;
using WebFrontend.Utils;

namespace WebFrontend.Components.UI.Button;

[ComponentMetadata(
    Description = "Displays a button or a component that looks like a button.",
    IsEntry = true,
    Group = nameof(Button))]
public partial class Button : ComponentBase
{
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public string Variant { get; set; } = "default";

    [Parameter]
    public string Size { get; set; } = "default";

    [Parameter]
    public string? Class { get; set; }

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public string Type { get; set; } = "button";

    [Parameter]
    public EventCallback<MouseEventArgs> OnClick { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    private string GetClass()
    {
        return ButtonStyles.Build(Variant, Size, Class);
    }
}
