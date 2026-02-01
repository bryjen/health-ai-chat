using Microsoft.AspNetCore.Components;
using WebFrontend.Components.UI.Select;

namespace WebFrontend.Components.Core.Location;

public partial class StateSelector : ComponentBase
{
    [CascadingParameter(Name = "LocationContext")]
    private LocationContext? Context { get; set; }
    
    [Parameter]
    public string? Value { get; set; }
    
    [Parameter]
    public EventCallback<string?> ValueChanged { get; set; }
    
    [Parameter]
    public IReadOnlyList<SelectOption>? Options { get; set; }
    
    [Parameter]
    public bool Disabled { get; set; }
    
    [Parameter]
    public string Placeholder { get; set; } = "Select State/Province";
    
    [Parameter]
    public string LabelText { get; set; } = "State/Province";
    
    [Parameter]
    public bool ShowLabel { get; set; } = true;
    
    [Parameter]
    public string? OuterClass { get; set; }
    
    [Parameter]
    public string? Class { get; set; }
    
    private IReadOnlyList<SelectOption> EffectiveOptions => Options ?? Context?.StateOptions ?? new List<SelectOption>();
    private string? EffectiveValue => Value ?? Context?.SelectedStateId;
    private bool EffectiveDisabled => Disabled || Context == null || string.IsNullOrWhiteSpace(Context.SelectedCountryId);
    
    protected override void OnParametersSet()
    {
        // Clear Value when context is cleared (parent changed)
        if (Context?.SelectedStateId == null && Value != null)
        {
            Value = null;
        }
        
        base.OnParametersSet();
    }
    
    private async Task OnValueChanged(string? value)
    {
        Value = value;
        
        if (Context != null)
        {
            Context.SelectedStateId = value;
            await Context.NotifyStateChangedAsync();
        }
        
        if (ValueChanged.HasDelegate)
        {
            await ValueChanged.InvokeAsync(value);
        }
    }
}
