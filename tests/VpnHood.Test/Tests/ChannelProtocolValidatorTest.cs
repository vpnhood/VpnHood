using VpnHood.Core.Client;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Messaging;

namespace VpnHood.Test.Tests;

[TestClass]
public class ChannelProtocolValidatorTest : TestBase
{
    // an older server's UDP port goes unused: its UDP has neither the server-reply bit nor the freshness mark
    [TestMethod]
    public void Udp_needs_a_server_of_its_protocol()
    {
        var olderServer = new HelloResponse {
            ErrorCode = SessionErrorCode.Ok,
            UdpPort = 1000,
            ProtocolVersion = UdpChannelTransmitter.MinProtocolVersion - 1
        };
        var newServer = new HelloResponse {
            ErrorCode = SessionErrorCode.Ok,
            UdpPort = 1000,
            ProtocolVersion = UdpChannelTransmitter.MinProtocolVersion
        };

        Assert.DoesNotContain(ChannelProtocol.Udp,
            ChannelProtocolValidator.GetChannelProtocols(olderServer, isQuicSupported: false));
        Assert.Contains(ChannelProtocol.Udp,
            ChannelProtocolValidator.GetChannelProtocols(newServer, isQuicSupported: false));
    }
}
