using Microsoft.AspNetCore.Components;
using WebFrontend.Components.UI.Select;
using WebFrontend.Models.Location;
using WebFrontend.Services;

namespace WebFrontend.Components.Core.Location;

public partial class LocationSelector : ComponentBase, IDisposable
{
    [Inject]
    private LocationService LocationService { get; set; } = default!;
    
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
    
    [Parameter]
    public LocationSelection? Value { get; set; }
    
    [Parameter]
    public EventCallback<LocationSelection> ValueChanged { get; set; }
    
    [Parameter]
    public int CountryId 
    { 
        get => _selectedCountryId; 
        set => _selectedCountryId = value; 
    }
    
    [Parameter]
    public EventCallback<int> CountryIdChanged { get; set; }
    
    [Parameter]
    public int StateId 
    { 
        get => _selectedStateId; 
        set => _selectedStateId = value; 
    }
    
    [Parameter]
    public EventCallback<int> StateIdChanged { get; set; }
    
    [Parameter]
    public int CityId 
    { 
        get => _selectedCityId; 
        set => _selectedCityId = value; 
    }
    
    [Parameter]
    public EventCallback<int> CityIdChanged { get; set; }
    
    [Parameter]
    public EventCallback<LocationSelection> OnSelectionChanged { get; set; }
    
    [Parameter]
    public EventCallback<LocationContext> ContextChanged { get; set; }
    
    private LocationContext _context = new();
    
    public LocationContext Context => _context;
    
    private List<Country> _countries = new();
    private List<State> _states = new();
    private List<City> _cities = new();
    
    private int _selectedCountryId;
    private int _selectedStateId;
    private int _selectedCityId;
    
    protected override async Task OnInitializedAsync()
    {
        _context.StateChanged += OnContextStateChanged;
        await LoadCountriesAsync();
        
        if (Value != null)
        {
            _selectedCountryId = Value.CountryId;
            _selectedStateId = Value.StateId;
            _selectedCityId = Value.CityId;
            
            if (_selectedCountryId > 0)
            {
                await LoadStatesAsync();
            }
            
            if (_selectedStateId > 0)
            {
                await LoadCitiesAsync();
            }
        }
        
        UpdateContext();
        
        if (ContextChanged.HasDelegate)
        {
            await ContextChanged.InvokeAsync(_context);
        }
    }
    
    private async Task LoadCountriesAsync()
    {
        _countries = await LocationService.GetCountriesAsync();
        UpdateContext(countryOptions: _countries.Select(c => new SelectOption(c.Id.ToString(), c.Name)).ToList());
    }
    
    private async Task LoadStatesAsync()
    {
        _states = await LocationService.GetStatesByCountryAsync(_selectedCountryId);
        UpdateContext(stateOptions: _states.Select(s => new SelectOption(s.Id.ToString(), s.Name)).ToList());
    }
    
    private async Task LoadCitiesAsync()
    {
        UpdateContext(isLoadingCities: true);
        await InvokeAsync(StateHasChanged);
        
        _cities = await LocationService.GetCitiesByStateAsync(_selectedStateId);
        UpdateContext(
            cityOptions: _cities.Select(c => new SelectOption(c.Id.ToString(), c.Name)).ToList(),
            isLoadingCities: false
        );
    }
    
    private void UpdateContext(
        List<SelectOption>? countryOptions = null,
        List<SelectOption>? stateOptions = null,
        List<SelectOption>? cityOptions = null,
        bool isLoadingCities = false)
    {
        var oldContext = _context;
        
        _context = new LocationContext
        {
            CountryOptions = countryOptions ?? oldContext.CountryOptions,
            StateOptions = stateOptions ?? oldContext.StateOptions,
            CityOptions = cityOptions ?? oldContext.CityOptions,
            SelectedCountryId = _selectedCountryId > 0 ? _selectedCountryId.ToString() : null,
            SelectedStateId = _selectedStateId > 0 ? _selectedStateId.ToString() : null,
            SelectedCityId = _selectedCityId > 0 ? _selectedCityId.ToString() : null,
            SelectedCountryName = _selectedCountryId > 0 
                ? _countries.FirstOrDefault(c => c.Id == _selectedCountryId)?.Name 
                : null,
            SelectedStateName = _selectedStateId > 0 
                ? _states.FirstOrDefault(s => s.Id == _selectedStateId)?.Name 
                : null,
            SelectedCityName = _selectedCityId > 0 
                ? _cities.FirstOrDefault(c => c.Id == _selectedCityId)?.Name 
                : null,
            IsLoadingCities = isLoadingCities
        };
        
        _context.StateChanged += OnContextStateChanged;
        oldContext.StateChanged -= OnContextStateChanged;
        
        StateHasChanged();
    }
    
    private async Task OnContextStateChanged()
    {
        // Handle country change
        var countryId = ParseId(_context.SelectedCountryId);
        if (countryId != _selectedCountryId)
        {
            _selectedCountryId = countryId;
            _selectedStateId = 0;
            _selectedCityId = 0;
            _states.Clear();
            _cities.Clear();
            
            UpdateContext(
                stateOptions: new List<SelectOption>(),
                cityOptions: new List<SelectOption>()
            );
            
            if (countryId > 0)
            {
                await LoadStatesAsync();
            }
        }
        
        // Handle state change
        var stateId = ParseId(_context.SelectedStateId);
        if (stateId != _selectedStateId && _selectedCountryId > 0)
        {
            _selectedStateId = stateId;
            _selectedCityId = 0;
            _cities.Clear();
            
            UpdateContext(cityOptions: new List<SelectOption>());
            
            if (stateId > 0)
            {
                await LoadCitiesAsync();
            }
        }
        
        // Handle city change
        var cityId = ParseId(_context.SelectedCityId);
        if (cityId != _selectedCityId && _selectedStateId > 0)
        {
            _selectedCityId = cityId;
            UpdateContext();
        }
        
        await NotifyChangesAsync();
        
        if (ContextChanged.HasDelegate)
        {
            await ContextChanged.InvokeAsync(_context);
        }
    }
    
    private static int ParseId(string? idString) => 
        idString != null && int.TryParse(idString, out var id) ? id : 0;
    
    private async Task NotifyChangesAsync()
    {
        var selection = new LocationSelection
        {
            CountryId = _selectedCountryId,
            StateId = _selectedStateId,
            CityId = _selectedCityId
        };
        
        if (ValueChanged.HasDelegate)
        {
            Value = selection;
            await ValueChanged.InvokeAsync(selection);
        }
        
        if (CountryIdChanged.HasDelegate)
        {
            await CountryIdChanged.InvokeAsync(_selectedCountryId);
        }
        
        if (StateIdChanged.HasDelegate)
        {
            await StateIdChanged.InvokeAsync(_selectedStateId);
        }
        
        if (CityIdChanged.HasDelegate)
        {
            await CityIdChanged.InvokeAsync(_selectedCityId);
        }
        
        if (OnSelectionChanged.HasDelegate)
        {
            await OnSelectionChanged.InvokeAsync(selection);
        }
    }
    
    public void Dispose()
    {
        _context.StateChanged -= OnContextStateChanged;
    }
}
