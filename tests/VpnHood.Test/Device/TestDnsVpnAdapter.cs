using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Test.Device;

// a null adapter whose network under the tunnel has the DNS servers a test gives, and the IP versions it gives
public class TestDnsVpnAdapter(IReadOnlyList<IPAddress> dnsServers) : TestNullVpnAdapter
{
    public IReadOnlyList<IpVersion> NetworkIpVersions { get; init; } = [IpVersion.IPv4, IpVersion.IPv6];

    public override IReadOnlyList<IPAddress> GetPrimaryAdapterDnsServers() => dnsServers;

    public override bool IsIpVersionSupported(IpVersion ipVersion) => NetworkIpVersions.Contains(ipVersion);

    // a test's resolver is on the loopback, which no tunnel takes, so its socket needs no protection
    public override bool ProtectSocket(Socket socket, IPAddress remoteAddress) =>
        IPAddress.IsLoopback(remoteAddress) || base.ProtectSocket(socket, remoteAddress);
}
