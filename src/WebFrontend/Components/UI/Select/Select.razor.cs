using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using WebFrontend.Services;
using WebFrontend.Utils;

namespace WebFrontend.Components.UI.Select;

public record SelectOption(string Value, string Label, bool Disabled = false);

[ComponentMetadata(
    Description = "Custom dropdown-style select component.",
    IsEntry = true,
    Group = nameof(Select))]
public partial class Select
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
    private ElementReference _triggerRef;
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
            // Prefer matching option label when we have a value
            if (!string.IsNullOrEmpty(Value) && Options is not null)
            {
                foreach (var option in Options)
                {
                    if (string.Equals(option.Value, Value, StringComparison.Ordinal))
                    {
                        return option.Label;
                    }
                }

                // Fallback: show raw value if no option matches
                return Value!;
            }

            // No value yet – fall back to placeholder or generic text
            return Placeholder ?? "Select an option";
        }
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
    }

    private SelectOption? FindOption(string? value)
    {
        if (Options is null || value is null) return null;

        foreach (var option in Options)
        {
            if (option.Value == value)
                return option;
        }

        return null;
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
            return $"{baseClasses} bg-primary text-primary-foreground";
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
        await ScrollLockService.LockAsync();
        await DetermineDirectionAsync();
        StateHasChanged();
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
        await ScrollLockService.UnlockAsync();
        StateHasChanged();
    }

    private async Task DetermineDirectionAsync()
    {
        // Explicit overrides from caller
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

        // Auto mode: prefer JS-based viewport measurement when available
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

        // Fallback: open downward
        _openUpwards = false;
    }

    private string GetPositionClasses()
    {
        // When opening upwards, align to bottom of trigger; otherwise below it.
        return _openUpwards
            ? "bottom-full mb-1 left-0 origin-bottom"
            : "mt-1 left-0 origin-top";
    }
}

public enum DropdownDirection
{
    Auto,
    Down,
    Up
}

