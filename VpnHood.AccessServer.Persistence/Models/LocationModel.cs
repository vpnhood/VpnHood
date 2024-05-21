namespace VpnHood.AccessServer.Persistence.Models;

public class LocationModel
{
    public const string UnknownCode = "-";

    public required int LocationId { get; set; }
    public required string CountryName { get; init; }
    public required string CountryCode { get; init; }
    public required string? RegionName { get; init; }
    public required string RegionCode { get; init; }
    public required string? CityName { get; init; }
    public required string CityCode { get; init; }
    public required string? ContinentCode { get; init; }

    public string ToPath()
    {
        return $"{CountryCode}/{RegionName ?? CityName ?? ""}".Trim('/');
    }

    public virtual ICollection<ServerModel>? Servers { get; set; }
}