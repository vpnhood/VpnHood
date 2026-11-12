namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>
/// Represents a proxy endpoint with its status.
/// </summary>
public class ProxyEndPointInfo
{
    /// <summary>Gets or sets the endpoint.</summary>
    public required ProxyEndPoint EndPoint { get; set; }

    /// <summary>Gets or sets the endpoint's status. The default is a new, unused one.</summary>
    public ProxyEndPointStatus Status { get; set; } = new();
}