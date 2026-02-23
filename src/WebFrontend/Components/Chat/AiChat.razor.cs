using Microsoft.AspNetCore.Components;
using ShadcnBlazor.Components.Shared;
using WebFrontend.Components.Chat.Models;
using WebFrontend.Components.Chat.Services;

namespace WebFrontend.Components.Chat;

public partial class AiChat : ShadcnComponentBase
{
    [Inject]
    public required ChatOrchestrator ChatOrchestrator { get; set; }

    private readonly ChatComponentState _chatComponentState = new();

    protected override void OnInitialized()
    {
        ChatOrchestrator.OnStateChange += OnChatStateChange;
    }

    public void Dispose()
    {
        ChatOrchestrator.OnStateChange -= OnChatStateChange;
    }

    private async Task OnChatStateChange()
    {
        await InvokeAsync(StateHasChanged);
    }

    private void SubmitPrompt(string prompt)
    {
        _ = ChatOrchestrator.SendPromptAsync(prompt);
    }
}
