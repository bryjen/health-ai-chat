using Microsoft.AspNetCore.Components;
using WebFrontend.Components.UI.Select;

namespace WebFrontend.Components.Core.Location;

public partial class CountrySelector : ComponentBase
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
    public string Placeholder { get; set; } = "Select Country";
    
    [Parameter]
    public string LabelText { get; set; } = "Country";
    
    [Parameter]
    public bool ShowLabel { get; set; } = true;
    
    [Parameter]
    public string? OuterClass { get; set; }
    
    [Parameter]
    public string? Class { get; set; }
    
    private IReadOnlyList<SelectOption> EffectiveOptions => Options ?? Context?.CountryOptions ?? new List<SelectOption>();
    private string? EffectiveValue => Value ?? Context?.SelectedCountryId;
    
    private async Task OnValueChanged(string? value)
    {
        Value = value;
        
        if (Context != null)
        {
            Context.SelectedCountryId = value;
            await Context.NotifyStateChangedAsync();
        }
        
        if (ValueChanged.HasDelegate)
        {
            await ValueChanged.InvokeAsync(value);
        }
        
        StateHasChanged();
    }
}
