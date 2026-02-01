using System.Text.Json.Serialization;

namespace Web.Common.DTOs.Location;

public class StateDto
{
    [JsonPropertyName("id")] public int Id { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    [JsonPropertyName("country_id")] public int CountryId { get; set; }

    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
}
