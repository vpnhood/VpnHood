using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering;
using VpnHood.Net.Packets;

namespace VpnHood.Test.Tests;

[TestClass]
public class DomainFilteringServiceTest : TestBase
{
    [TestMethod]
    public void The_close_closes_the_QUIC_SNI_service()
    {
        var service = new DomainFilteringService(new StaticDomainFilter(nextFilter: null), new EventId(),
            tlsBufferSize: 1000) {
            IsEnabled = true
        };

        service.Dispose();
        using var ipPacket = PacketBuilder.BuildUdp(IPAddress.Parse("10.0.0.2"), IPAddress.Parse("10.0.0.3"),
            sourcePort: 50000, destinationPort: 443, payload: new byte[16]);
        Assert.ThrowsExactly<ObjectDisposedException>(() => service.ProcessPacket(ipPacket));
    }
}
