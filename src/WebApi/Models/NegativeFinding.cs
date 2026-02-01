using System.Diagnostics.CodeAnalysis;

namespace WebApi.Models;

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
public class NegativeFinding
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public int? EpisodeId { get; set; } // Optional link to episode
    public required string SymptomName { get; set; } // What's absent
    public DateTime ReportedAt { get; set; }
    
    // Navigation properties
    public User? User { get; set; }
    public Episode? Episode { get; set; }
}
