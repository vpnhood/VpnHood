using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.AppLib.Api.Sessions;

// Which resolvers the session ended up with, and whether they are inside the tunnel.

/// <summary>
/// Represents a session's DNS: its servers, where they came from, and whether the tunnel carries them.
/// </summary>
public class DnsConfig
{
    /// <summary>Gets where the servers came from.</summary>
    public required DnsSelection DnsSelection { get; init; }

    /// <summary>
    /// Gets a value indicating whether the plan is for the tunnel to carry the queries to the servers; a server at the
    /// VPN server's own address is reached outside it all the same, as that address always is.
    /// </summary>
    public required bool IsIncludedInVpn { get; init; }

    /// <summary>
    /// Gets a value indicating whether the user's own servers were set aside, as servers the tunnel cannot carry while
    /// DNS must stay inside it.
    /// </summary>
    public required bool IsUserSuppressed { get; init; }

    /// <summary>Gets the DNS servers.</summary>
    [JsonConverter(typeof(ArrayConverter<IPAddress, IPAddressConverter>))]
    public required IPAddress[] DnsServers { get; init; }
}
