using WebFrontend.Components.Chat.Services.ChatService;
using WebFrontend.Components.Chat.Services.StreamResponse;

namespace WebFrontend.Components.Chat.Services;

public static class AiChatServiceExtensions
{
    public static IServiceCollection AddAiChat(this IServiceCollection services)
    {
        services.AddScoped<IChatService, TestChatService>();
        services.AddScoped<IStreamResponseParserService, StreamResponseParserService>();
        services.AddScoped<ChatOrchestrator>();
        return services;
    }
}
