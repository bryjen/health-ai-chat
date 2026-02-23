namespace WebFrontend.Components.Chat.Services.StreamResponse;

public interface IStreamResponseParser
{
    void AppendChunk(string chunk);
}
