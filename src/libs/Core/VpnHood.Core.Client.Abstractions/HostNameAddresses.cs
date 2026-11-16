using System.Net;

namespace VpnHood.Core.Client.Abstractions;

/// <summary>
/// Represents a host name's addresses, known before a connection asks for them.
/// </summary>
/// <remarks>
/// The check that found a server's move had the name's new addresses, and the connection right after takes them rather
/// than asking the system's resolver, which may still ask through the old VPN: Android tears it down a moment after the
/// session ends.
/// </remarks>
public class HostNameAddresses
{
    /// <summary>Gets the host name.</summary>
    public required string HostName { get; init; }

    /// <summary>Gets the name's addresses.</summary>
    public required IReadOnlyList<IPAddress> Addresses { get; init; }
}
