using System.Net;
using System.Net.NetworkInformation;

namespace VpnHood.Test.Providers;

// The base class keeps its list to itself, filled by .NET alone
public class TestGatewayIPAddressInformationCollection(IReadOnlyList<IPAddress> addresses)
    : GatewayIPAddressInformationCollection
{
    private readonly IReadOnlyList<GatewayIPAddressInformation> _items =
        addresses.Select(x => new TestGatewayIPAddressInformation(x)).ToArray();

    public override int Count => _items.Count;
    public override GatewayIPAddressInformation this[int index] => _items[index];
    public override IEnumerator<GatewayIPAddressInformation> GetEnumerator() => _items.GetEnumerator();
}
