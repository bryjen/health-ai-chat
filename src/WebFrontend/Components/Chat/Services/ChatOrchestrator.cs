using System.Collections.ObjectModel;
using WebApi.ApiWrapper.Services;
using WebFrontend.Components.Chat.Models;
using WebFrontend.Components.Chat.Services.ChatService;
using WebFrontend.Components.Chat.Services.StreamResponse;

namespace WebFrontend.Components.Chat.Services;

public class ChatOrchestrator(IChatService chatService, IStreamResponseParserService streamParserService, IConversationsApiClient conversationsApiClient)
{
    public bool LockUserInput { get; set; } = false;
    public bool IsLoadingConversation { get; private set; } = false;
    public ObservableCollection<ChatMessage> Messages { get; set; } = new();

    public event Func<Task> OnStateChange = null!;
    public event Func<string, Task>? OnConversationStarted;

    public void ResetConversation()
    {
        chatService.ConversationId = null;
        Messages.Clear();
    }

    public async Task LoadConversationAsync(string conversationId)
    {
        if (chatService.ConversationId == conversationId && Messages.Count > 0)
            return;

        IsLoadingConversation = true;
        Messages.Clear();
        await OnStateChange.Invoke();

        try
        {
            var conversation = await conversationsApiClient.GetConversationByIdAsync(conversationId);
            if (conversation != null)
            {
                chatService.ConversationId = conversationId;
                foreach (var msg in conversation.Messages)
                {
                    if (msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                    {
                        Messages.Add(new UserChatMessage(msg.Content));
                    }
                    else
                    {
                        var aiMsg = new AiChatMessage(string.Empty);
                        var parser = streamParserService.CreateStream(aiMsg.Components);
                        parser.AppendChunk(msg.Content);
                        Messages.Add(aiMsg);
                    }
                }
            }
        }
        catch
        {
            // leave messages empty on error
        }
        finally
        {
            IsLoadingConversation = false;
            await OnStateChange.Invoke();
        }
    }

    public async Task SendPromptAsync(string promptRaw, CancellationToken cancellationToken = default)
    {
        Console.WriteLine(promptRaw);

        var userMsg = new UserChatMessage(promptRaw);
        LockUserInput = true;
        Messages.Add(userMsg);
        await OnStateChange.Invoke();

        var responseMsg = new AiChatMessage(string.Empty);
        var streamParser = streamParserService.CreateStream(responseMsg.Components);
        Messages.Add(responseMsg);

        var wasNewConversation = chatService.ConversationId is null;

        await foreach (var update in chatService.RunStreamingAsync(promptRaw, cancellationToken))
        {
            responseMsg.AppendContent(update);
            streamParser.AppendChunk(update);
            await OnStateChange.Invoke();
        }

        LockUserInput = false;
        await OnStateChange.Invoke();

        if (wasNewConversation && chatService.ConversationId is not null && OnConversationStarted is not null)
            await OnConversationStarted.Invoke(chatService.ConversationId);
    }
}
