using System.Diagnostics.CodeAnalysis;
using Pgvector;

namespace WebApi.Models;

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
public class MessageEmbedding
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public Guid UserId { get; set; }
    public Vector Embedding { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public Message? Message { get; set; }
    public User? User { get; set; }
}
