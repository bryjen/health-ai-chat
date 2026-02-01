namespace WebFrontend.Components.UI.Tabs;

/// <summary>
/// Shared context for tabs value and orientation.
/// </summary>
public sealed class TabsContext
{
    public TabsContext(string? value, Action<string?> onChange, string orientation)
    {
        // Initialize fields directly to avoid invoking callbacks before they're assigned.
        _value = value;
        _onChange = onChange;
        Orientation = orientation;
    }

    private string? _value;
    private readonly Action<string?>? _onChange;

    public string? Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            _onChange?.Invoke(value);
        }
    }

    public string Orientation { get; }
}

