namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>
/// Represents a proxy endpoint as the endpoint store keeps it: the endpoint, its status and its country.
/// </summary>
public class ProxyEndPointRecord
{
    /// <summary>Gets or sets the endpoint.</summary>
    public required ProxyEndPoint EndPoint { get; set; }

    /// <summary>Gets or sets the endpoint's status. The default is a new, unused one.</summary>
    public ProxyEndPointStatus Status { get; set; } = new();

    /// <summary>Gets or sets the code of the country the app found the endpoint in, or <see langword="null"/>.</summary>
    public string? CountryCode { get; set; }

    /// <summary>Gets the record's endpoint and status, without its country.</summary>
    /// <returns>An info that shares the record's endpoint and status objects.</returns>
    public ProxyEndPointInfo ToInfo() => new() { EndPoint = EndPoint, Status = Status };

    /// <summary>Makes a record of an endpoint and its status.</summary>
    /// <param name="info">The endpoint and its status, whose objects the record shares.</param>
    /// <param name="countryCode">The endpoint's country code, or <see langword="null"/>.</param>
    /// <returns>The record.</returns>
    public static ProxyEndPointRecord FromInfo(ProxyEndPointInfo info, string? countryCode = null) =>
        new() { EndPoint = info.EndPoint, Status = info.Status, CountryCode = countryCode };
}
