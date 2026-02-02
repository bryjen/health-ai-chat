using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using WebFrontend.Components.UI.Select;

namespace WebFrontend.Components.Core.Chat;

public partial class ChatInput : ComponentBase
{
    [Parameter]
    public string InputText { get; set; } = string.Empty;
    [Parameter]
    public EventCallback<string> InputTextChanged { get; set; }
    [Parameter]
    public bool IsLoading { get; set; }
    [Parameter]
    public bool IsConnected { get; set; }

    [Parameter]
    public EventCallback OnSubmit { get; set; }

    // Model selection is owned here so the page doesn't have to care.
    private readonly IReadOnlyList<SelectOption> _modelOptions =
    [
        new("sonnet-4-5", "Sonnet 4.5"),
        new("opus-4", "Opus 4"),
        new("haiku-4", "Haiku 4"),
        new("claude-3-5-sonnet", "Claude 3.5 Sonnet")
    ];

    private string? _selectedModel = "sonnet-4-5";

    private async Task HandleFormSubmit()
    {
        await OnSubmit.InvokeAsync();
    }

    private async Task OnInputChanged(ChangeEventArgs e)
    {
        var value = e.Value?.ToString() ?? string.Empty;
        await InputTextChanged.InvokeAsync(value);
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !e.ShiftKey)
        {
            await OnSubmit.InvokeAsync();
        }
    }

    private Task HandleModelChanged(string? value)
    {
        _selectedModel = value;
        return Task.CompletedTask;
    }
}
