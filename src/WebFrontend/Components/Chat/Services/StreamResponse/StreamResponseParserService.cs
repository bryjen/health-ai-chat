using WebFrontend.Components.Chat.Models;

namespace WebFrontend.Components.Chat.Services.StreamResponse;

public class StreamResponseParserService : IStreamResponseParserService
{
    public IStreamResponseParser CreateStream(IList<MessageComponent> components) =>
        new StreamResponseParser(components);
}
