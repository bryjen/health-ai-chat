using System.Text.Json.Serialization;

namespace WebFrontend.Models.Location;

public class Country
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("iso2")]
    public string Iso2 { get; set; } = string.Empty;
}

public class State
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("country_id")]
    public int CountryId { get; set; }
    
    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }
}

public class City
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

public class LocationSelection
{
    public int CountryId { get; set; }
    public int StateId { get; set; }
    public int CityId { get; set; }
    
    public bool IsEmpty => CountryId == 0 && StateId == 0 && CityId == 0;
}
