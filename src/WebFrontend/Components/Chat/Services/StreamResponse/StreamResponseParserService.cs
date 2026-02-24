using System.Text.Json;
using WebFrontend.Components.Chat.Models;

namespace WebFrontend.Components.Chat.Services.StreamResponse;

public class StreamResponseParserService(JsonSerializerOptions jsonOptions) : IStreamResponseParserService
{
    public IStreamResponseParser CreateStream(IList<MessageComponent> components) =>
        new StreamResponseParser(components, jsonOptions);
}
