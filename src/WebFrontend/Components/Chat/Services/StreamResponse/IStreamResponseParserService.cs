using WebFrontend.Components.Chat.Models;

namespace WebFrontend.Components.Chat.Services.StreamResponse;

public interface IStreamResponseParserService
{
    IStreamResponseParser CreateStream(IList<MessageComponent> components);
}
