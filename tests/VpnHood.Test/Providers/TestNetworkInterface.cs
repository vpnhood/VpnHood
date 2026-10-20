using System.Net;
using System.Net.NetworkInformation;

namespace VpnHood.Test.Providers;

// A network interface as an OS reports it, with only what IPAddressUtil's LAN filter reads
public class TestNetworkInterface(
    string name,
    NetworkInterfaceType networkInterfaceType,
    string description,
    OperationalStatus operationalStatus = OperationalStatus.Up,
    IReadOnlyList<IPAddress>? gateways = null)
    : NetworkInterface
{
    public override string Name => name;
    public override string Description => description;
    public override NetworkInterfaceType NetworkInterfaceType => networkInterfaceType;
    public override OperationalStatus OperationalStatus => operationalStatus;
    public override IPInterfaceProperties GetIPProperties() => new TestIPInterfaceProperties(gateways ?? []);
}
