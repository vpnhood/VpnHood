using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Net.IpLocations;

/// <summary>
/// Represents where an IP address is: its country, and its region and city where known.
/// </summary>
public class IpLocation
{
    /// <summary>Gets the IP address.</summary>
    [JsonConverter(typeof(IPAddressConverter))]
    public required IPAddress IpAddress { get; init; }

    /// <summary>Gets the country's English name.</summary>
    public required string CountryName { get; init; }

    /// <summary>Gets the country's two-letter ISO 3166 code, such as US.</summary>
    public required string CountryCode { get; init; }

    /// <summary>Gets the region's name, such as a state or a province, or <see langword="null"/> when unknown.</summary>
    public required string? RegionName { get; init; }

    /// <summary>Gets the city's name, or <see langword="null"/> when unknown.</summary>
    public required string? CityName { get; init; }
}