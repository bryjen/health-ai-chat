namespace WebFrontend.Services;

public enum ToastType
{
    Default,
    Success,
    Error,
    Warning,
    Info
}

public class Toast
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ToastType Type { get; set; } = ToastType.Default;
    public int Duration { get; set; } = 5000; // milliseconds
    public bool Dismissible { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRemoving { get; set; } = false;
}

public class ToastService
{
    private readonly List<Toast> _toasts = new();
    private event Action? OnToastsChanged;

    public IReadOnlyList<Toast> Toasts => _toasts.AsReadOnly();

    public void Subscribe(Action callback)
    {
        OnToastsChanged += callback;
    }

    public void Unsubscribe(Action callback)
    {
        OnToastsChanged -= callback;
    }

    public void Show(string title, string? description = null, ToastType type = ToastType.Default, int? duration = null)
    {
        var toast = new Toast
        {
            Title = title,
            Description = description,
            Type = type,
            Duration = duration ?? (type == ToastType.Error ? 7000 : 5000)
        };

        _toasts.Add(toast);
        NotifyStateChanged();

        if (toast.Duration > 0)
        {
            _ = Task.Delay(toast.Duration).ContinueWith(async _ =>
            {
                await DismissWithAnimationAsync(toast.Id);
            });
        }
    }

    public void ShowSuccess(string title, string? description = null, int? duration = null)
    {
        Show(title, description, ToastType.Success, duration);
    }

    public void ShowError(string title, string? description = null, int? duration = null)
    {
        Show(title, description, ToastType.Error, duration);
    }

    public void ShowWarning(string title, string? description = null, int? duration = null)
    {
        Show(title, description, ToastType.Warning, duration);
    }

    public void ShowInfo(string title, string? description = null, int? duration = null)
    {
        Show(title, description, ToastType.Info, duration);
    }

    public async Task DismissWithAnimationAsync(Guid id)
    {
        var toast = _toasts.FirstOrDefault(t => t.Id == id);
        if (toast != null && !toast.IsRemoving)
        {
            toast.IsRemoving = true;
            NotifyStateChanged();
            
            // Wait for fade-out animation (300ms)
            await Task.Delay(300);
            
            _toasts.Remove(toast);
            NotifyStateChanged();
        }
    }

    public void Dismiss(Guid id)
    {
        var toast = _toasts.FirstOrDefault(t => t.Id == id);
        if (toast != null)
        {
            if (!toast.IsRemoving)
            {
                _ = DismissWithAnimationAsync(id);
            }
        }
    }

    public void DismissAll()
    {
        _toasts.Clear();
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        OnToastsChanged?.Invoke();
    }
}
