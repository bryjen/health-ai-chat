namespace WebFrontend.Components.UI.Accordion;

/// <summary>
/// Shared context for accordion item state between trigger and content.
/// </summary>
public sealed class AccordionItemContext
{
    public AccordionItemContext(bool open, Action<bool> setOpen)
    {
        Open = open;
        _setOpen = setOpen;
    }

    private bool _open;
    private readonly Action<bool> _setOpen;

    public bool Open
    {
        get => _open;
        set
        {
            if (_open == value) return;
            _open = value;
            _setOpen(value);
        }
    }

    public void Toggle() => Open = !Open;
}

