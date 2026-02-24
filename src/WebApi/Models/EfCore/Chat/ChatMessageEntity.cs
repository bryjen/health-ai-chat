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
}
