using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace WebApi.Models;

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
public class AssessmentEpisodeLink
{
    public int AssessmentId { get; set; }
    public int EpisodeId { get; set; }
    
    public decimal Weight { get; set; } // 0-1, diagnostic contribution
    public string? Reasoning { get; set; } // Why this symptom matters
    
    // Navigation properties
    [JsonIgnore]
    public Assessment? Assessment { get; set; }
    [JsonIgnore]
    public Episode? Episode { get; set; }
}
