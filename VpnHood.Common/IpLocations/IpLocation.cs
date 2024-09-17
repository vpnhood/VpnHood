using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Common.IpLocations;

public class IpLocation
{
    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress IpAddress { get; init; }

    public required string CountryName { get; init; }
    public required string CountryCode { get; init; }
    public required string? RegionName { get; init; }
    public required string? CityName { get; init; }
}