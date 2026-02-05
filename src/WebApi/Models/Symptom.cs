using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace WebApi.Models;

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
public class Symptom
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }                // "Cough", "Fever", "Headache"
    public string? Description { get; set; }        // "Dry cough, worse at night"
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Navigation properties
    [JsonIgnore]
    public User? User { get; set; }
    [JsonIgnore]
    public ICollection<Episode> Episodes { get; set; } = new List<Episode>();
}
