using Microsoft.AspNetCore.Components;
using WebFrontend.Components.UI.Select;

namespace WebFrontend.Components.Core.Location;

public partial class CitySelector : ComponentBase
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
    public string? Placeholder { get; set; }
    
    [Parameter]
    public string LabelText { get; set; } = "City";
    
    [Parameter]
    public bool ShowLabel { get; set; } = true;
    
    [Parameter]
    public string? OuterClass { get; set; }
    
    [Parameter]
    public string? Class { get; set; }
    
    private IReadOnlyList<SelectOption> EffectiveOptions => Options ?? Context?.CityOptions ?? new List<SelectOption>();
    private string? EffectiveValue => Value ?? Context?.SelectedCityId;
    private bool EffectiveDisabled => Disabled || Context == null || string.IsNullOrWhiteSpace(Context.SelectedStateId);
    private string EffectivePlaceholder => Placeholder ?? Context?.CityPlaceholder ?? "Select City";
    
    protected override void OnParametersSet()
    {
        // Clear Value when context is cleared (parent changed)
        if (Context?.SelectedCityId == null && Value != null)
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
            Context.SelectedCityId = value;
            await Context.NotifyStateChangedAsync();
        }
        
        if (ValueChanged.HasDelegate)
        {
            await ValueChanged.InvokeAsync(value);
        }
    }
}
