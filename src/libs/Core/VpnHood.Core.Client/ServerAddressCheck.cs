using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client;

// Whether the server has left the address its name gave the session: a server behind a dynamic name (DDNS) may
// change its address while a session is up, and the session cannot follow it, as the address is fixed at its start,
// in its connections, its UDP and the adapter's routes. Only for an address the name gave: a token's own address, a
// redirect's or a custom one never moves with the name.
// The name is asked of the network's DNS servers, those of the network under the tunnel, over a socket the adapter
// protects, so the lookup bypasses the tunnel (on Apple, every socket of the VPN's extension goes around its tunnel);
// where it cannot protect one, the name is not asked, as the lookup would go into the tunnel it checks. A name that
// gets no answer, as in an outage of the device's own network, is no move: the session waits, as it did.
// The name is asked for the addresses of the session's IP version first: a resolver that drops the other version's
// query would leave this one's answer to read as a move. The other version is asked only where an answer of the
// session's held none, as a name that moved to the other version alone gives, and the device's network carries it.
internal class ServerAddressCheck(string hostName, IPAddress serverAddress, IVpnAdapter vpnAdapter)
{
    private static readonly TimeSpan DnsServerTimeout = TimeSpan.FromSeconds(5);

    // the DNS servers' port; a test's resolver listens on one of its own
    internal int DnsPort { get; init; } = 53;

    // the name's addresses, where they no longer hold the session's; null while they do or the name gave no answer
    public async Task<HostNameAddresses?> TryGetNewAddresses(CancellationToken cancellationToken)
    {
        var addresses = await TryResolve(cancellationToken).Vhc();
        if (addresses.Count == 0 || addresses.Contains(serverAddress) || addresses.Any(IsTranslatedFrom))
            return null;

        VhLogger.Instance.LogInformation(
            "The server's name no longer gives the session's address. HostName: {HostName}, Address: {Address}, " +
            "NewAddresses: {NewAddresses}",
            VhLogger.FormatHostName(hostName), VhLogger.Format(serverAddress),
            string.Join(", ", addresses.Select(x => VhLogger.Format(x))));
        return new HostNameAddresses { HostName = hostName, Addresses = addresses };
    }

    // The first DNS server's addresses of the name: of the session's IP version, or, where its answer held none, of the
    // other, where the device's network carries it: a move to the other version alone is none for a device without it,
    // whose reconnect would find no server. So on an IPv6 network behind NAT64, whose session reached the server at an
    // address made from the name's IPv4 one, a new IPv4 address is no move, as the device cannot reach it as it is.
    // Asked at each check, as Linux's adapter protects a socket only once started.
    private async Task<IReadOnlyList<IPAddress>> TryResolve(CancellationToken cancellationToken)
    {
        var otherAddressFamily = serverAddress.AddressFamily == AddressFamily.InterNetworkV6
            ? AddressFamily.InterNetwork
            : AddressFamily.InterNetworkV6;

        var isOtherUsable = IsCarried(otherAddressFamily);

        var dnsServers = TryGetDnsServers();
        foreach (var dnsServer in dnsServers) {
            var addresses = await TryResolveByDnsServer(dnsServer, serverAddress.AddressFamily, cancellationToken)
                .Vhc();

            if (addresses is { Count: > 0 })
                return addresses;

            if (addresses is null || !isOtherUsable)
                continue;

            var otherAddresses = await TryResolveByDnsServer(dnsServer, otherAddressFamily, cancellationToken).Vhc();
            if (otherAddresses is { Count: > 0 })
                return otherAddresses;
        }

        VhLogger.Instance.LogInformation(
            "The server's name got no address, so the session keeps waiting. DnsServers: {DnsServers}",
            dnsServers.Count);

        return [];
    }

    // Whether the session's IPv6 address is the IPv4 address given, as a NAT64 network's device makes one (RFC 6052,
    // a /96 prefix): where the network's DNS servers make no AAAA answer, the device makes it from the A record, and the
    // name's IPv4 address is then the session's own, not a move. A server's own IPv6 address that ends in its IPv4 one
    // reads so too, which leaves its move to that IPv4 address alone no move
    private bool IsTranslatedFrom(IPAddress address)
    {
        if (serverAddress.AddressFamily != AddressFamily.InterNetworkV6 ||
            address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        Span<byte> serverBytes = stackalloc byte[16];
        Span<byte> addressBytes = stackalloc byte[4];
        return serverAddress.TryWriteBytes(serverBytes, out _) && address.TryWriteBytes(addressBytes, out _) &&
               serverBytes[12..].SequenceEqual(addressBytes);
    }

    private bool IsCarried(AddressFamily addressFamily)
    {
        var ipVersion = addressFamily == AddressFamily.InterNetworkV6 ? IpVersion.IPv6 : IpVersion.IPv4;
        try {
            return vpnAdapter.IsIpVersionSupported(ipVersion);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not tell whether the network carries an IP version. IpVersion: {IpVersion}",
                ipVersion);

            return false;
        }
    }

    private IReadOnlyList<IPAddress> TryGetDnsServers()
    {
        try {
            return vpnAdapter.GetPrimaryAdapterDnsServers();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not get the network's DNS servers.");
            return [];
        }
    }

    // a DNS server's addresses of the name of an IP version, none where its answer held none, or null where it gave none
    private async Task<IReadOnlyList<IPAddress>?> TryResolveByDnsServer(IPAddress dnsServer,
        AddressFamily addressFamily, CancellationToken cancellationToken)
    {
        try {
            using var udpClient = new UdpClient(dnsServer.AddressFamily);
            if (!vpnAdapter.ProtectSocket(udpClient.Client, dnsServer)) {
                VhLogger.Instance.LogDebug("Could not protect a socket to a DNS server. DnsServer: {DnsServer}",
                    VhLogger.Format(dnsServer));
                return null;
            }

            return await DnsUtils.GetHostAddresses(hostName, new IPEndPoint(dnsServer, DnsPort), udpClient,
                addressFamily, DnsServerTimeout, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogInformation(ex, "A DNS server gave no answer for the server's name. DnsServer: {DnsServer}",
                VhLogger.Format(dnsServer));
            return null;
        }
    }
}
