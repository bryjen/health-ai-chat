using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ShadcnBlazor.Components.Shared;
using WebFrontend.Components.Chat.Models;
using WebFrontend.Components.Chat.Services;

namespace WebFrontend.Components.Chat;

public partial class AiChat : ShadcnComponentBase
{
    [Inject]
    public required IWebAssemblyHostEnvironment Environment { get; set; }

    [Inject]
    public required ChatOrchestrator ChatOrchestrator { get; set; }

    [Inject]
    public required NavigationManager Navigation { get; set; }

    [Parameter]
    public string? ConversationId { get; set; }

    private string? _loadedConversationId;
    private readonly ChatComponentState _chatComponentState = new();

    protected override void OnInitialized()
    {
        ChatOrchestrator.OnStateChange += OnChatStateChange;
        ChatOrchestrator.OnConversationStarted += OnConversationStarted;

        if (Environment.IsDevelopment())
        {
            _chatComponentState.ShowToolCalls = true;
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!string.IsNullOrEmpty(ConversationId) && ConversationId != _loadedConversationId)
        {
            _loadedConversationId = ConversationId;
            await ChatOrchestrator.LoadConversationAsync(ConversationId);
        }
        else if (string.IsNullOrEmpty(ConversationId) && _loadedConversationId != null)
        {
            _loadedConversationId = null;
            ChatOrchestrator.ResetConversation();
        }
    }

    public void Dispose()
    {
        ChatOrchestrator.OnStateChange -= OnChatStateChange;
        ChatOrchestrator.OnConversationStarted -= OnConversationStarted;
    }

    private async Task OnChatStateChange()
    {
        await InvokeAsync(StateHasChanged);
    }

    private Task OnConversationStarted(string conversationId)
    {
        _loadedConversationId = conversationId;
        Navigation.NavigateTo($"/chat?conversation={conversationId}", replace: true);
        return Task.CompletedTask;
    }

    private void SubmitPrompt(string prompt)
    {
        _ = ChatOrchestrator.SendPromptAsync(prompt);
    }
}
