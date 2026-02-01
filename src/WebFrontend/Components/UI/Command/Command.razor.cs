using Microsoft.AspNetCore.Components;
using WebFrontend.Utils;

namespace WebFrontend.Components.UI.Command;

[ComponentMetadata(
    Description = "Command palette container for searchable command lists.",
    IsEntry = true,
    Group = nameof(Command))]
public partial class Command
{
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public string? Class { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }
}

