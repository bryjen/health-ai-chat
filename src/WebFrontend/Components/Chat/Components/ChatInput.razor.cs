using System.Drawing;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using ShadcnBlazor.Components.Shared.Models.Options;
using ShadcnBlazor.Components.Shared.Services;
using WebFrontend.Components.Chat.Models;
using Size = ShadcnBlazor.Components.Shared.Models.Enums.Size;

namespace WebFrontend.Components.Chat.Components;

public partial class ChatInput : ComponentBase, IAsyncDisposable
{
    [Inject]
    public IKeyInterceptorService KeyInterceptor { get; set; } = null!;

    [CascadingParameter(Name = nameof(Size))]
    public Size Size { get; set; } = Size.Md;

    [CascadingParameter(Name = nameof(ChatComponentState))]
    public ChatComponentState ChatComponentState { get; set; } = new();

    [CascadingParameter(Name = "InvokeParentStateHasChanged")]
    public EventCallback InvokeParentStateHasChanged { get; set; } = new();

    [Parameter]
    public EventCallback<string> SubmitPrompt { get; set; }

    private string _prompt = string.Empty;
    private bool _extendedThinkingToggle = true;

    private string? _framework = "dotnet";

    private bool _showRaw = true;

    private async Task CheckedChanged()
    {
        ChatComponentState.RawView = !ChatComponentState.RawView;
        await InvokeParentStateHasChanged.InvokeAsync();
    }

    private async Task ShowUnknownTagsChanged()
    {
        ChatComponentState.ShowUnknownTags = !ChatComponentState.ShowUnknownTags;
        await InvokeParentStateHasChanged.InvokeAsync();
    }

    private async Task ShowToolCallsChanged()
    {
        ChatComponentState.ShowToolCalls = !ChatComponentState.ShowToolCalls;
        await InvokeParentStateHasChanged.InvokeAsync();
    }


#region KeyInterceptor
    private readonly string _elementId = "chat-input-" + Guid.NewGuid().ToString("N")[..8];
    private DotNetObjectReference<ChatInput>? _dotNetRef;

    [JSInvokable]
    public async Task OnKeyDown(string elementId, KeyInterceptorEventArgs args)
    {
        if (args.Key == "Enter" && !args.CtrlKey && !args.MetaKey && !args.ShiftKey)  // submit on plain Enter
        {
            await SubmitPrompt.InvokeAsync(_prompt);
            _prompt = string.Empty;
            await InvokeAsync(StateHasChanged);
        }
        else if (args.Key == "Escape")  // clear on escape
        {
            _prompt = string.Empty;
            await InvokeAsync(StateHasChanged);
        }
        // Ctrl/Shift+Enter falls through — textarea inserts newline naturally
    }

    [JSInvokable]
    public async Task OnKeyUp(string elementId, KeyInterceptorEventArgs args)
    {
        await Task.CompletedTask;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNetRef = DotNetObjectReference.Create(this);
            var options = new KeyInterceptorOptions(
                new KeyOptions("Enter", subscribeDown: true, preventDown: "key+none", stopDown: "key+none"),
                new KeyOptions("Escape", subscribeDown: true)
            );
            try
            {
                await KeyInterceptor.ConnectAsync(_elementId, _dotNetRef, options);
            }
            catch
            {
                // Element not mounted yet
            }
        }
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (_dotNetRef is not null)
        {
            await KeyInterceptor.DisconnectAsync(_elementId);
            _dotNetRef.Dispose();
        }
    }
#endregion
}
