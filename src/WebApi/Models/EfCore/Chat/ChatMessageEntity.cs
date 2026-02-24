using Microsoft.Extensions.VectorData;

namespace WebApi.Models.EfCore.Chat;

public class ChatMessageEntity
{
    [VectorStoreKey]
    public string? Key { get; set; }

    [VectorStoreData]
    public string? SessionId { get; set; }

    [VectorStoreData]
    public DateTime? Timestamp { get; set; }

    [VectorStoreData]
    public string? SerializedMessage { get; set; }

    [VectorStoreData]
    public string? MessageText { get; set; }

    // Out-of-band tags emitted during plugin execution (e.g. <SymptomCreated>).
    // Stored separately so they can be prepended verbatim on conversation replay
    // without going through the message formatter (which would wrap them in <Text>).
    public string? EmittedTags { get; set; }
}
