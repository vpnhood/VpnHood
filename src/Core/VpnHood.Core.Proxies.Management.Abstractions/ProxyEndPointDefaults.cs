namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>
/// Provides the values <see cref="ProxyEndPointParser.ParseHostToUrl"/> uses for the parts a proxy's address leaves
/// out; <see langword="null"/> leaves a part out, or, for the port, to the protocol's default. A scheme must come from
/// the address or from here.
/// </summary>
public class ProxyEndPointDefaults
{
    /// <summary>Gets or sets whether the endpoint is enabled; <see langword="false"/> marks it disabled.</summary>
    public bool? IsEnabled { get; set; }

    /// <summary>Gets or sets the protocol, for an address with no scheme.</summary>
    public ProxyProtocol? Protocol { get; set; }

    /// <summary>
    /// Gets or sets the port, for an address with no port of its own or with its scheme's standard one, as 80 for
    /// <c>http</c>.
    /// </summary>
    public int? Port { get; set; }

    /// <summary>Gets or sets the user name, for an address with none.</summary>
    public string? Username { get; set; }

    /// <summary>Gets or sets the password, for an address with none.</summary>
    public string? Password { get; set; }
}