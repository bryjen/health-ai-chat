using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using WebFrontend.Components.UI.Select;
using WebFrontend.Services;
using WebFrontend.Utils;

namespace WebFrontend.Components.UI.Combobox;

[ComponentMetadata(
    Description = "Combobox component with search functionality.",
    IsEntry = true,
    Group = nameof(Combobox))]
public partial class Combobox : ComponentBase, IAsyncDisposable
{
    [Parameter]
    public IReadOnlyList<SelectOption> Options { get; set; } = Array.Empty<SelectOption>();
    
    [Parameter]
    public string? Value { get; set; }
    
    [Parameter]
    public EventCallback<string?> ValueChanged { get; set; }
    
    [Parameter]
    public bool Disabled { get; set; }
    
    [Parameter]
    public string? Placeholder { get; set; }
    
    [Parameter]
    public string? Label { get; set; }
    
    [Parameter]
    public DropdownDirection Direction { get; set; } = DropdownDirection.Auto;
    
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    [Parameter]
    public string? OuterClass { get; set; }
    
    [Parameter]
    public string? Class { get; set; }

    [Inject]
    private ScrollLockService ScrollLockService { get; set; } = default!;
    
    [Inject]
    private IJSRuntime JsRuntime { get; set; } = default!;

    private bool _open;
    private bool _isClosing;
    private bool _openUpwards;
    private string _searchQuery = string.Empty;
    private ElementReference _triggerRef;
    private ElementReference _searchInputRef;
    private IJSObjectReference? _jsModule;

    private string AnimationClass =>
        _isClosing
            ? _openUpwards
                ? "animate-[select-out-up_140ms_cubic-bezier(0.2,0.8,0.2,1)_forwards]"
                : "animate-[select-out-down_140ms_cubic-bezier(0.2,0.8,0.2,1)_forwards]"
            : _openUpwards
                ? "animate-[select-in-up_160ms_cubic-bezier(0.2,0.8,0.2,1)_forwards]"
                : "animate-[select-in-down_160ms_cubic-bezier(0.2,0.8,0.2,1)_forwards]";

    private string DisplayText
    {
        get
        {
            if (!string.IsNullOrEmpty(Value) && Options is not null)
            {
                foreach (var option in Options)
                {
                    if (string.Equals(option.Value, Value, StringComparison.Ordinal))
                    {
                        return option.Label;
                    }
                }
                return Value!;
            }
            return Placeholder ?? "Select an option";
        }
    }

    private IReadOnlyList<SelectOption> FilteredOptions
    {
        get
        {
            var options = Options ?? Array.Empty<SelectOption>();
            
            if (string.IsNullOrWhiteSpace(_searchQuery))
            {
                return options;
            }

            var query = _searchQuery.Trim().ToLowerInvariant();
            return options
                .Where(opt => opt.Label?.ToLowerInvariant().Contains(query) == true && !opt.Disabled)
                .ToList();
        }
    }

    private int _lastOptionsCount = 0;

    protected override void OnParametersSet()
    {
        // Force re-render when Options change, especially if they were empty before
        var currentCount = Options?.Count ?? 0;
        
        if (currentCount != _lastOptionsCount)
        {
            _lastOptionsCount = currentCount;
            // Always re-render when options change, not just when open
            StateHasChanged();
        }
        base.OnParametersSet();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && _jsModule is null)
        {
            try
            {
                _jsModule = await JsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/select.js");
            }
            catch (JSException)
            {
                // JS module not available; fall back to downward-only.
            }
        }

        // Focus search input when dropdown opens
        if (_open && !_isClosing && _searchInputRef.Context != null)
        {
            await FocusSearchInputAsync();
        }
    }

    private bool IsOptionSelected(SelectOption option)
    {
        if (Value is null || option.Value is null)
            return false;

        return string.Equals(option.Value, Value, StringComparison.Ordinal);
    }

    private string GetItemClasses(SelectOption option, bool isSelected)
    {
        var baseClasses = "flex w-full items-center px-3 py-2 text-sm rounded-md transition-colors";

        if (option.Disabled)
        {
            return $"{baseClasses} text-muted-foreground/60 cursor-not-allowed";
        }

        if (isSelected)
        {
            // Use explicit white text on primary background for better visibility
            return $"{baseClasses} bg-primary text-white";
        }

        return $"{baseClasses} cursor-pointer text-foreground hover:bg-muted/70";
    }

    private async Task ToggleAsync()
    {
        if (Disabled) return;

        if (_open)
        {
            await StartCloseAnimationAsync();
            return;
        }

        _isClosing = false;
        _open = true;
        _searchQuery = string.Empty; // Reset search when opening
        await ScrollLockService.LockAsync();
        await DetermineDirectionAsync();
        StateHasChanged();
        
        // Focus search input after render
        await Task.Delay(50);
        await FocusSearchInputAsync();
    }

    private Task CloseAsync()
    {
        if (!_open) return Task.CompletedTask;
        return StartCloseAnimationAsync();
    }

    private async Task SelectAsync(string value)
    {
        if (Disabled) return;

        if (ValueChanged.HasDelegate)
        {
            await ValueChanged.InvokeAsync(value);
        }

        await StartCloseAnimationAsync();
    }

    private async Task StartCloseAnimationAsync()
    {
        _isClosing = true;
        StateHasChanged();

        await Task.Delay(140);

        _open = false;
        _isClosing = false;
        _searchQuery = string.Empty; // Clear search when closing
        await ScrollLockService.UnlockAsync();
        StateHasChanged();
    }

    private async Task DetermineDirectionAsync()
    {
        if (Direction == DropdownDirection.Up)
        {
            _openUpwards = true;
            return;
        }

        if (Direction == DropdownDirection.Down)
        {
            _openUpwards = false;
            return;
        }

        if (_jsModule != null)
        {
            try
            {
                var result = await _jsModule.InvokeAsync<string>("chooseSelectDirection", _triggerRef);
                _openUpwards = string.Equals(result, "up", StringComparison.OrdinalIgnoreCase);
                return;
            }
            catch (JSException)
            {
                // Ignore and fall through to default
            }
        }

        _openUpwards = false;
    }

    private string GetPositionClasses()
    {
        return _openUpwards
            ? "bottom-full mb-1 left-0 origin-bottom"
            : "mt-1 left-0 origin-top";
    }

    private void OnSearchInput(ChangeEventArgs e)
    {
        _searchQuery = e.Value?.ToString() ?? string.Empty;
        StateHasChanged(); // Explicitly trigger re-render when search query changes
    }

    private async Task HandleSearchKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            await CloseAsync();
        }
        else if (e.Key == "Enter" && FilteredOptions.Count == 1)
        {
            // If only one filtered option, select it on Enter
            await SelectAsync(FilteredOptions[0].Value);
        }
    }

    private async Task FocusSearchInputAsync()
    {
        try
        {
            // Use a small delay to ensure the DOM is ready
            await Task.Delay(50);
            
            // Focus using JS interop - query the element by data attribute
            await JsRuntime.InvokeVoidAsync("eval", @"
                const searchInput = document.querySelector('[data-combobox-search]');
                if (searchInput) {
                    searchInput.focus();
                }
            ");
        }
        catch
        {
            // Ignore focus errors - focus will happen naturally when user clicks
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_jsModule != null)
        {
            try
            {
                await _jsModule.DisposeAsync();
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }
}
