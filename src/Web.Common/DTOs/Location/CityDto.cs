using System.Text.Json.Serialization;

namespace Web.Common.DTOs.Location;

public class CityDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("state_id")]
    public int StateId { get; set; }
    
    [JsonPropertyName("country_id")]
    public int CountryId { get; set; }
}
