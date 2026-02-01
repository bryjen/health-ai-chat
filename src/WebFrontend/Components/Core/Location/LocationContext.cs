using WebFrontend.Components.UI.Select;
using WebFrontend.Models.Location;

namespace WebFrontend.Components.Core.Location;

public class LocationContext
{
    public List<SelectOption> CountryOptions { get; set; } = new();
    public List<SelectOption> StateOptions { get; set; } = new();
    public List<SelectOption> CityOptions { get; set; } = new();
    
    public string? SelectedCountryId { get; set; }
    public string? SelectedStateId { get; set; }
    public string? SelectedCityId { get; set; }
    
    public (int? Id, string? Name) Country => 
        SelectedCountryId != null && int.TryParse(SelectedCountryId, out var id) 
            ? (id, SelectedCountryName) 
            : (null, null);
    
    public (int? Id, string? Name) State => 
        SelectedStateId != null && int.TryParse(SelectedStateId, out var id) 
            ? (id, SelectedStateName) 
            : (null, null);
    
    public (int? Id, string? Name) City => 
        SelectedCityId != null && int.TryParse(SelectedCityId, out var id) 
            ? (id, SelectedCityName) 
            : (null, null);
    
    public string? SelectedCountryName { get; set; }
    public string? SelectedStateName { get; set; }
    public string? SelectedCityName { get; set; }
    
    public bool IsLoadingCities { get; set; }
    public bool StatesDisabled => StateOptions.Count == 0;
    public bool CitiesDisabled => CityOptions.Count == 0 || IsLoadingCities;
    
    public string CityPlaceholder => IsLoadingCities ? "Loading cities..." : "Select City";
    
    public event Func<Task>? StateChanged;
    
    public async Task NotifyStateChangedAsync()
    {
        if (StateChanged != null)
        {
            await StateChanged.Invoke();
        }
    }
}
