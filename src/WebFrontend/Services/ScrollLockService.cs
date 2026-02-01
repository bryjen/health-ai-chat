using Microsoft.JSInterop;

namespace WebFrontend.Services;

/// <summary>
/// Owns body scroll lock state. Multiple clients (Dialog, Dropdown, etc.) call Lock/Unlock;
/// scroll is actually locked only when the first client locks and unlocked when the last client unlocks.
/// </summary>
public class ScrollLockService
{
    private readonly IJSRuntime _jsRuntime;
    private int _lockCount;

    public ScrollLockService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task LockAsync()
    {
        _lockCount++;
        if (_lockCount == 1)
            await _jsRuntime.InvokeVoidAsync("lockScroll");
    }

    public async Task UnlockAsync()
    {
        if (_lockCount == 0)
            return;
        _lockCount--;
        if (_lockCount == 0)
            await _jsRuntime.InvokeVoidAsync("unlockScroll");
    }
}
