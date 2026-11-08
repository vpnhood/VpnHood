using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client;

// Whether the server has left the address its name gave the session: a server behind a dynamic name (DDNS) may
// change its address while a session is up, and the session cannot follow it, as the address is fixed at its start,
// in its connections, its UDP and the adapter's routes. Only for an address the name gave: a token's own address, a
// redirect's or a custom one never moves with the name.
// The name is asked of the network's DNS servers, those of the network under the tunnel, over a socket the adapter
// protects, so the lookup bypasses the tunnel; where it cannot protect one, as on iOS, the name is not asked, as the
// lookup would go into the tunnel it checks. A name that gets no answer, as in an outage of the device's own network,
// is no move: the session waits, as it did.
internal class ServerAddressCheck(string hostName, IPAddress serverAddress, IVpnAdapter vpnAdapter)
{
    private static readonly TimeSpan DnsServerTimeout = TimeSpan.FromSeconds(5);

    // the name's addresses, where they no longer hold the session's; null while they do or the name gave no answer
    public async Task<HostNameAddresses?> TryGetNewAddresses(CancellationToken cancellationToken)
    {
        var addresses = await TryResolve(cancellationToken).Vhc();
        if (addresses.Count == 0 || addresses.Contains(serverAddress))
            return null;

        VhLogger.Instance.LogInformation(
            "The server's name no longer gives the session's address. HostName: {HostName}, Address: {Address}, " +
            "NewAddresses: {NewAddresses}",
            VhLogger.FormatHostName(hostName), VhLogger.Format(serverAddress),
            string.Join(", ", addresses.Select(x => VhLogger.Format(x))));
        return new HostNameAddresses { HostName = hostName, Addresses = addresses };
    }

    // The first DNS server's answer. Asked at each check, as Linux's adapter protects a socket only once started.
    private async Task<IReadOnlyList<IPAddress>> TryResolve(CancellationToken cancellationToken)
    {
        if (!vpnAdapter.CanProtectSocket) {
            VhLogger.Instance.LogDebug("The adapter cannot protect a socket, so the server's name is not asked.");
            return [];
        }

        var dnsServers = TryGetDnsServers();
        foreach (var dnsServer in dnsServers) {
            var addresses = await TryResolveByDnsServer(dnsServer, cancellationToken).Vhc();
            if (addresses.Count > 0)
                return addresses;
        }

        VhLogger.Instance.LogInformation(
            "The server's name got no answer, so the session keeps waiting. DnsServers: {DnsServers}",
            dnsServers.Count);
        return [];
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

    private async Task<IReadOnlyList<IPAddress>> TryResolveByDnsServer(IPAddress dnsServer,
        CancellationToken cancellationToken)
    {
        try {
            using var udpClient = new UdpClient(dnsServer.AddressFamily);
            if (!vpnAdapter.ProtectSocket(udpClient.Client, dnsServer)) {
                VhLogger.Instance.LogDebug("Could not protect a socket to a DNS server. DnsServer: {DnsServer}",
                    VhLogger.Format(dnsServer));
                return [];
            }

            var hostEntry = await DnsUtils.GetHostEntry(hostName, new IPEndPoint(dnsServer, 53), udpClient,
                DnsServerTimeout, cancellationToken).Vhc();
            return hostEntry.AddressList;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogInformation(ex, "A DNS server gave no answer for the server's name. DnsServer: {DnsServer}",
                VhLogger.Format(dnsServer));
            return [];
        }
    }
}
