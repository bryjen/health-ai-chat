namespace WebFrontend.Models.Location;

public class LocationSelection
{
    public int CountryId { get; set; }
    public int StateId { get; set; }
    public int CityId { get; set; }

    public bool IsEmpty => CountryId == 0 && StateId == 0 && CityId == 0;
}
