using System.Net;
using System.Net.NetworkInformation;

namespace VpnHood.Test.Providers;

// An interface's IP settings, with only the gateways IPAddressUtil's LAN filter reads
public class TestIPInterfaceProperties(IReadOnlyList<IPAddress> gateways) : IPInterfaceProperties
{
    public override GatewayIPAddressInformationCollection GatewayAddresses { get; } =
        new TestGatewayIPAddressInformationCollection(gateways);

    public override bool IsDnsEnabled => throw new NotSupportedException();
    public override string DnsSuffix => throw new NotSupportedException();
    public override bool IsDynamicDnsEnabled => throw new NotSupportedException();
    public override UnicastIPAddressInformationCollection UnicastAddresses => throw new NotSupportedException();
    public override MulticastIPAddressInformationCollection MulticastAddresses => throw new NotSupportedException();
    public override IPAddressInformationCollection AnycastAddresses => throw new NotSupportedException();
    public override IPAddressCollection DnsAddresses => throw new NotSupportedException();
    public override IPAddressCollection DhcpServerAddresses => throw new NotSupportedException();
    public override IPAddressCollection WinsServersAddresses => throw new NotSupportedException();
    public override IPv4InterfaceProperties GetIPv4Properties() => throw new NotSupportedException();
    public override IPv6InterfaceProperties GetIPv6Properties() => throw new NotSupportedException();
}
