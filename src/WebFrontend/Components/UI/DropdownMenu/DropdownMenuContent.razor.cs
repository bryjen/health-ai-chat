using Microsoft.AspNetCore.Components;
using WebFrontend.Components.UI.Shared;

namespace WebFrontend.Components.UI.DropdownMenu;

public partial class DropdownMenuContent : ComponentBase
{
    [CascadingParameter] public DropdownMenu? ParentMenu { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public int SideOffset { get; set; } = 4;

    [Parameter] public string? Class { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    protected override void OnInitialized()
    {
        ParentMenu?.RegisterContent(this);
    }

    protected override void OnParametersSet()
    {
        ParentMenu?.RegisterContent(this);
    }

    private string GetClass()
    {
        var baseClasses =
            "bg-card text-card-foreground z-50 max-h-[--radix-dropdown-menu-content-available-height] " +
            "w-fit overflow-x-hidden overflow-y-auto rounded-xl border border-border p-1 shadow-lg";

        return ClassBuilder.Merge(baseClasses, Class);
    }
}
