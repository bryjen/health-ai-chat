using Microsoft.AspNetCore.Components;

namespace WebFrontend.Services;

/// <summary>
/// Service for managing dialogs that are rendered outside the layout via DialogProvider.
/// </summary>
public class DialogService
{
    private readonly Dictionary<string, DialogInstance> _dialogs = new();

    public event Action? OnDialogsChanged;

    /// <summary>
    /// Registers a dialog instance with the service.
    /// </summary>
    public void RegisterDialog(string dialogId, DialogInstance instance)
    {
        _dialogs[dialogId] = instance;
        OnDialogsChanged?.Invoke();
    }

    /// <summary>
    /// Unregisters a dialog instance.
    /// </summary>
    public void UnregisterDialog(string dialogId)
    {
        if (_dialogs.Remove(dialogId))
        {
            OnDialogsChanged?.Invoke();
        }
    }

    /// <summary>
    /// Gets all currently registered dialogs.
    /// </summary>
    public IEnumerable<DialogInstance> GetDialogs()
    {
        return _dialogs.Values;
    }

    /// <summary>
    /// Gets a specific dialog instance.
    /// </summary>
    public DialogInstance? GetDialog(string dialogId)
    {
        return _dialogs.TryGetValue(dialogId, out var instance) ? instance : null;
    }
}

/// <summary>
/// Represents a dialog instance managed by DialogService.
/// </summary>
public class DialogInstance
{
    public string DialogId { get; set; } = string.Empty;
    public bool Open { get; set; }
    public RenderFragment? Content { get; set; }
    public Action? OnClose { get; set; }
}
