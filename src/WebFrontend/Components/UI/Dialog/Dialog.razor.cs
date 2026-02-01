using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using WebFrontend.Services;
using WebFrontend.Utils;

namespace WebFrontend.Components.UI.Dialog;

[ComponentMetadata(
    Description = "Modal dialog root component for layered content and overlays.",
    IsEntry = true,
    Group = nameof(Dialog))]
public partial class Dialog : ComponentBase, IAsyncDisposable
{
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public bool Open { get; set; }

    [Parameter]
    public EventCallback<bool> OpenChanged { get; set; }

    [Parameter]
    public string? DialogId { get; set; }


    [Inject]
    private IJSRuntime JsRuntime { get; set; } = null!;

    [Inject]
    private DialogService DialogService { get; set; } = null!;

    [Inject]
    private ScrollLockService ScrollLockService { get; set; } = null!;

    private IJSObjectReference? _jsModule;
    private DotNetObjectReference<Dialog>? _dotNetRef;
    private bool _isInitialized;
    private DialogContent? _contentComponent;

    protected override void OnInitialized()
    {
        DialogId ??= Guid.NewGuid().ToString();
    }

    public void RegisterContent(DialogContent contentComponent)
    {
        _contentComponent = contentComponent;
    }

    private bool _isClosing = false;
    private bool _closedByInternalLogic = false;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNetRef = DotNetObjectReference.Create(this);
            _jsModule = await JsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/dialog.js");
        }

        // Update registration in OnAfterRenderAsync as well (in case Open changed via binding)
        if (Open && _contentComponent != null && !_isClosing)
        {
            var instance = new DialogInstance
            {
                DialogId = DialogId!,
                Open = true,
                Content = BuildContentFragment(),
                OnClose = () => _ = CloseAsync()
            };
            DialogService.RegisterDialog(DialogId!, instance);
        }
        // Don't unregister immediately when closing - wait for animation to complete
        // Unregistration happens in CloseAsync after animation delay

        if (_jsModule != null && _dotNetRef != null)
        {
            if (Open)
            {
                // Initialize dialog only once, after elements are rendered
                if (!_isInitialized)
                {
                    // Wait a bit for DOM to update - especially for DialogProvider to render
                    await Task.Delay(50);
                    await _jsModule.InvokeVoidAsync("initializeDialog", DialogId, _dotNetRef);
                    _isInitialized = true;
                }
                await _jsModule.InvokeVoidAsync("openDialog", DialogId);
            }
            else if (_isInitialized && !_isClosing && !_closedByInternalLogic)
            {
                // Open was set to false via external binding (e.g. @bind-Open) - handle close here
                _isClosing = true;
                await _jsModule.InvokeVoidAsync("closeDialog", DialogId);
                await Task.Delay(300);
                DialogService.UnregisterDialog(DialogId!);
                await ScrollLockService.UnlockAsync();
                _isClosing = false;
            }
        }
    }

    private RenderFragment BuildContentFragment()
    {
        if (_contentComponent == null) return _ => { };

        return builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", _contentComponent.GetContentClass());
            builder.AddMultipleAttributes(2, _contentComponent.AdditionalAttributes ?? new Dictionary<string, object>());

            if (_contentComponent.ShowCloseButton)
            {
                builder.OpenElement(3, "button");
                builder.AddAttribute(4, "data-slot", "dialog-close");
                builder.AddAttribute(5, "class", "ring-offset-background focus:ring-ring absolute top-4 right-4 rounded-xs opacity-70 transition-opacity hover:opacity-100 focus:ring-2 focus:ring-offset-2 focus:outline-hidden disabled:pointer-events-none [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4");
                builder.AddAttribute(6, "onclick", EventCallback.Factory.Create<Microsoft.AspNetCore.Components.Web.MouseEventArgs>(_contentComponent, _contentComponent.HandleClose));
                builder.OpenElement(7, "svg");
                builder.AddAttribute(8, "class", "size-4");
                builder.AddAttribute(9, "fill", "none");
                builder.AddAttribute(10, "stroke", "currentColor");
                builder.AddAttribute(11, "viewBox", "0 0 24 24");
                builder.OpenElement(12, "path");
                builder.AddAttribute(13, "stroke-linecap", "round");
                builder.AddAttribute(14, "stroke-linejoin", "round");
                builder.AddAttribute(15, "stroke-width", "2");
                builder.AddAttribute(16, "d", "M6 18L18 6M6 6l12 12");
                builder.CloseElement();
                builder.CloseElement();
                builder.OpenElement(17, "span");
                builder.AddAttribute(18, "class", "sr-only");
                builder.AddContent(19, "Close");
                builder.CloseElement();
                builder.CloseElement();
            }

            builder.AddContent(20, _contentComponent.ChildContent);
            builder.CloseElement();
        };
    }

    public async Task OpenAsync()
    {
        _closedByInternalLogic = false;
        Open = true;
        await OpenChanged.InvokeAsync(Open);
        await ScrollLockService.LockAsync();

        // Register with DialogService immediately when opening
        if (_contentComponent != null)
        {
            var instance = new DialogInstance
            {
                DialogId = DialogId!,
                Open = true,
                Content = BuildContentFragment(),
                OnClose = () => _ = CloseAsync()
            };
            DialogService.RegisterDialog(DialogId!, instance);
        }

        // Force re-render to ensure DialogProvider gets updated
        StateHasChanged();
        // Give DialogProvider time to render before JS animations
        await Task.Delay(50);
    }

    public async Task CloseAsync()
    {
        _closedByInternalLogic = true;
        _isClosing = true;

        // Start the fade-out animation first
        if (_jsModule != null && _isInitialized)
        {
            await _jsModule.InvokeVoidAsync("closeDialog", DialogId);
        }

        // Wait for the animation to complete (250ms transition + small buffer)
        await Task.Delay(300);

        // Now actually close and unregister
        Open = false;
        await OpenChanged.InvokeAsync(Open);

        // Unregister from service after animation completes
        DialogService.UnregisterDialog(DialogId!);

        await ScrollLockService.UnlockAsync();

        _isClosing = false;
        StateHasChanged();
    }

    [JSInvokable]
    public void HandleEscape()
    {
        _ = CloseAsync();
    }

    [JSInvokable]
    public void HandleOverlayClick()
    {
        _ = CloseAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (DialogId != null)
        {
            DialogService.UnregisterDialog(DialogId);
        }
        if (_jsModule != null && DialogId != null)
        {
            await _jsModule.InvokeVoidAsync("disposeDialog", DialogId);
            await _jsModule.DisposeAsync();
        }
        _dotNetRef?.Dispose();
    }
}
