using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using WebFrontend.Components.Core.Location;
using WebFrontend.Components.UI.Select;
using WebFrontend.Models.Location;
using WebFrontend.Services;

namespace WebFrontend.Pages;

public partial class Components : ComponentBase
{
    [Inject] private IJSRuntime JsRuntime { get; set; } = default!;
    [Inject] private ScrollLockService ScrollLockService { get; set; } = default!;
    [Inject] private ToastService ToastSvc { get; set; } = default!;

    private bool _switchChecked = false;
    private bool _switchChecked2 = false;
    private bool _switchChecked3 = false;
    private bool _checkboxChecked = false;
    private bool _checkboxChecked2 = false;
    private bool _checkboxChecked3 = false;
    public bool DialogOpen = false;
    private bool _dropdownOpen = false;
    private const string Username = "shadcn";
    private bool _scrollLocked = false;

    private string _activeDashboardTab = "overview";
    private string? _selectedClinic;
    private string? _symptomNotes;
    private DateOnly? _appointmentDate;

    private int _progressValue = 40;

    private bool _sheetOpen;

    private LocationSelection _locationSelection = new();
    private LocationContext? _locationContext;

    private readonly IReadOnlyList<SelectOption> _clinicOptions = new[]
    {
        new SelectOption("downtown", "Downtown Health Center"),
        new SelectOption("north", "Northside Clinic"),
        new SelectOption("telehealth", "Telehealth Only")
    };

    private string? _selectedCountry;
    private readonly IReadOnlyList<SelectOption> _countryOptions = new[]
    {
        new SelectOption("ar", "Argentina"),
        new SelectOption("au", "Australia"),
        new SelectOption("br", "Brazil"),
        new SelectOption("ca", "Canada"),
        new SelectOption("cn", "China"),
        new SelectOption("co", "Colombia"),
        new SelectOption("eg", "Egypt"),
        new SelectOption("fr", "France"),
        new SelectOption("de", "Germany"),
        new SelectOption("in", "India"),
        new SelectOption("it", "Italy"),
        new SelectOption("jp", "Japan"),
        new SelectOption("mx", "Mexico"),
        new SelectOption("nl", "Netherlands"),
        new SelectOption("ru", "Russia"),
        new SelectOption("sa", "Saudi Arabia"),
        new SelectOption("za", "South Africa"),
        new SelectOption("kr", "South Korea"),
        new SelectOption("es", "Spain"),
        new SelectOption("se", "Sweden"),
        new SelectOption("ch", "Switzerland"),
        new SelectOption("th", "Thailand"),
        new SelectOption("tr", "Turkey"),
        new SelectOption("ae", "United Arab Emirates"),
        new SelectOption("gb", "United Kingdom"),
        new SelectOption("us", "United States"),
        new SelectOption("vn", "Vietnam")
    };

    private string _selectedClinicDisplay => _selectedClinic switch
    {
        "downtown" => "Downtown Health Center",
        "north" => "Northside Clinic",
        "telehealth" => "Telehealth Only",
        _ => _selectedClinic ?? "None"
    };

    private async Task ToggleScroll()
    {
        _scrollLocked = !_scrollLocked;
        if (_scrollLocked)
            await ScrollLockService.LockAsync();
        else
            await ScrollLockService.UnlockAsync();
    }

    private async Task OnDropdownOpenChanged(bool isOpen)
    {
        _dropdownOpen = isOpen;
        if (isOpen)
            await ScrollLockService.LockAsync();
        else
            await ScrollLockService.UnlockAsync();
    }

    private void OnSwitchCheckedChanged(bool value) => _switchChecked = value;
    private void OnSwitch2CheckedChanged(bool value) => _switchChecked2 = value;
    private void OnSwitch3CheckedChanged(bool value) => _switchChecked3 = value;
    private void OnCheckboxCheckedChanged(bool value) => _checkboxChecked = value;
    private void OnCheckbox2CheckedChanged(bool value) => _checkboxChecked2 = value;
    private void OnCheckbox3CheckedChanged(bool value) => _checkboxChecked3 = value;

    private void StepProgress()
    {
        _progressValue += 15;
        if (_progressValue > 100)
        {
            _progressValue = 0;
        }
    }

    private Task OnClinicChanged(string? value)
    {
        _selectedClinic = value;
        return Task.CompletedTask;
    }

    private Task OnCountryChanged(string? value)
    {
        _selectedCountry = value;
        return Task.CompletedTask;
    }

    private string _selectedCountryDisplay => _countryOptions.FirstOrDefault(o => o.Value == _selectedCountry)?.Label ?? (_selectedCountry ?? "None");

    private async Task OnSheetOpenChanged(bool isOpen)
    {
        _sheetOpen = isOpen;
        if (isOpen)
            await ScrollLockService.LockAsync();
        else
            await ScrollLockService.UnlockAsync();
    }

    private void ShowToast(ToastType type)
    {
        switch (type)
        {
            case ToastType.Success:
                ToastSvc.ShowSuccess("Appointment booked", "We sent a confirmation to your email.");
                break;
            case ToastType.Error:
                ToastSvc.ShowError("Booking failed", "Something went wrong while scheduling your visit.");
                break;
            case ToastType.Info:
                ToastSvc.ShowInfo("Reminder set", "We will remind you 24 hours before your appointment.");
                break;
            default:
                ToastSvc.Show("Notification");
                break;
        }
    }


    private Task OnLocationChanged(LocationSelection selection)
    {
        _locationSelection = selection;
        return Task.CompletedTask;
    }

    private string GetLocationDisplay()
    {
        if (_locationSelection.IsEmpty)
            return "None selected";
        
        var parts = new List<string>();
        
        if (_locationSelection.CountryId > 0)
        {
            var country = _locationContext?.Country ?? (null, null);
            var countryText = country.Name ?? _locationSelection.CountryId.ToString();
            parts.Add($"Country: {_locationSelection.CountryId} ({countryText})");
        }
        
        if (_locationSelection.StateId > 0)
        {
            var state = _locationContext?.State ?? (null, null);
            var stateText = state.Name ?? _locationSelection.StateId.ToString();
            parts.Add($"State: {_locationSelection.StateId} ({stateText})");
        }
        
        if (_locationSelection.CityId > 0)
        {
            var city = _locationContext?.City ?? (null, null);
            var cityText = city.Name ?? _locationSelection.CityId.ToString();
            parts.Add($"City: {_locationSelection.CityId} ({cityText})");
        }
        
        return string.Join(" • ", parts);
    }
    
    private Task OnLocationContextChanged(LocationContext context)
    {
        _locationContext = context;
        return Task.CompletedTask;
    }
}
