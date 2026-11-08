using System.Net;

namespace VpnHood.Core.Client.Abstractions;

// What a host name gave, known before a connect asks it: the check that found a server's move had the name's new
// addresses, and the connect right after takes them rather than asking the system's resolver, which may still ask
// through the old VPN: Android tears it down a moment after the session ends.
public class HostNameAddresses
{
    public required string HostName { get; init; }
    public required IReadOnlyList<IPAddress> Addresses { get; init; }
}
