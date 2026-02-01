using Microsoft.AspNetCore.Components;

namespace WebFrontend.Components.UI.DropdownMenu;

public partial class DropdownMenuCheckboxItem : ComponentBase
{
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public bool Checked { get; set; }

    [Parameter]
    public EventCallback<bool> CheckedChanged { get; set; }

    [Parameter]
    public string? Class { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    private async Task HandleClick()
    {
        Checked = !Checked;
        await CheckedChanged.InvokeAsync(Checked);
    }
}
