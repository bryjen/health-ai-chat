using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using WebFrontend.Utils;

namespace WebFrontend.Components.UI.Tooltip;

[ComponentMetadata(
    Description = "Tooltip root component for displaying rich hover and focus hints.",
    IsEntry = true,
    Group = nameof(Tooltip))]
public partial class Tooltip : ComponentBase, IAsyncDisposable
{
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public int DelayDuration { get; set; } = 0;
    [Parameter] public string? TooltipId { get; set; }

    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

    private IJSObjectReference? _jsModule;
    private DotNetObjectReference<Tooltip>? _dotNetRef;

    protected override void OnInitialized()
    {
        TooltipId ??= Guid.NewGuid().ToString();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNetRef = DotNetObjectReference.Create(this);
            _jsModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/tooltip.js");
            // Wait a bit for DOM to be ready, then find and mark the trigger
            await Task.Delay(50);
            await _jsModule.InvokeVoidAsync("markTooltipTrigger", TooltipId);
            await _jsModule.InvokeVoidAsync("initializeTooltip", TooltipId, _dotNetRef, DelayDuration);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_jsModule != null && TooltipId != null)
        {
            await _jsModule.InvokeVoidAsync("disposeTooltip", TooltipId);
            await _jsModule.DisposeAsync();
        }
        _dotNetRef?.Dispose();
    }
}
