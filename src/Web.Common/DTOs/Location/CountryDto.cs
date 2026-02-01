using System.Text.Json.Serialization;

namespace Web.Common.DTOs.Location;

public class CountryDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("iso2")]
    public string Iso2 { get; set; } = string.Empty;
}
