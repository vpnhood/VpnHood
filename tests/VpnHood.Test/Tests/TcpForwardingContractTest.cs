using System.Net;
using System.Text.Json;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Test.Tests;

// TCP forwarding was TcpProxy, and other versions still hold that name: the tunnel's client and server, and the
// access manager and config files a server reads. They read the old name, and the new one is written beside it.
[TestClass]
public class TcpForwardingContractTest : TestBase
{
    [TestMethod]
    public void A_hello_carries_both_names()
    {
        var hello = JsonSerializer.SerializeToNode(new HelloResponse {
            ErrorCode = SessionErrorCode.Ok,
            IsTcpForwardingSupported = false
        })!;

        Assert.IsFalse(hello["IsTcpForwardingSupported"]!.GetValue<bool>());
        Assert.IsFalse(hello["IsTcpProxySupported"]!.GetValue<bool>(), "An older client reads this name.");
    }

    [TestMethod]
    public void A_hello_from_an_older_server_is_read_by_the_old_name()
    {
        var node = JsonSerializer.SerializeToNode(new HelloResponse {
            ErrorCode = SessionErrorCode.Ok,
            IsTcpForwardingSupported = false
        })!.AsObject();
        node.Remove("IsTcpForwardingSupported");

        Assert.IsFalse(node.Deserialize<HelloResponse>()!.IsTcpForwardingSupported);
    }

    [TestMethod]
    public void A_hello_with_neither_name_allows_tcp_forwarding()
    {
        var node = JsonSerializer.SerializeToNode(new HelloResponse { ErrorCode = SessionErrorCode.Ok })!.AsObject();
        node.Remove("IsTcpForwardingSupported");
        node.Remove("IsTcpProxySupported");

        Assert.IsTrue(node.Deserialize<HelloResponse>()!.IsTcpForwardingSupported);
    }

    [TestMethod]
    public void The_request_keeps_its_byte_and_its_body()
    {
        var request = new TcpForwardingChannelRequest {
            RequestId = "request",
            SessionId = 1,
            SessionKey = new byte[16],
            DestinationEndPoint = new IPEndPoint(IPAddress.Loopback, 443)
        };

        Assert.AreEqual((byte)3, request.RequestCode);
        Assert.AreEqual("127.0.0.1:443", JsonSerializer.SerializeToNode(request)!["DestinationEndPoint"]!.GetValue<string>());
    }

    [TestMethod]
    public void Session_options_read_the_old_names()
    {
        var node = JsonSerializer.SerializeToNode(new SessionOptions {
            AllowTcpForwarding = false,
            StreamForwardingBufferSize = new TransferBufferSize(1000, 2000),
            UdpForwardingBufferSize = new TransferBufferSize(3000, 4000)
        })!.AsObject();
        node.Remove("AllowTcpForwarding");
        node.Remove("StreamForwardingBufferSize");
        node.Remove("UdpForwardingBufferSize");

        // false above all: lost, it would read as allowed
        var options = node.Deserialize<SessionOptions>()!;
        Assert.IsFalse(options.AllowTcpForwardingValue);
        Assert.AreEqual(new TransferBufferSize(1000, 2000), options.StreamForwardingBufferSize);
        Assert.AreEqual(new TransferBufferSize(3000, 4000), options.UdpForwardingBufferSize);
    }

    [TestMethod]
    [DataRow("""{"AllowTcpForwarding":true,"AllowTcpProxy":false}""")]
    [DataRow("""{"AllowTcpProxy":false,"AllowTcpForwarding":true}""")]
    public void Session_options_take_the_new_name_when_both_differ(string json)
    {
        Assert.IsTrue(JsonSerializer.Deserialize<SessionOptions>(json)!.AllowTcpForwardingValue);
    }

    [TestMethod]
    [DataRow("""{"AllowTcpForwarding":false,"AllowTcpProxy":true}""")]
    [DataRow("""{"AllowTcpProxy":true,"AllowTcpForwarding":false}""")]
    public void Session_options_keep_a_false_under_the_new_name(string json)
    {
        Assert.IsFalse(JsonSerializer.Deserialize<SessionOptions>(json)!.AllowTcpForwardingValue);
    }

    [TestMethod]
    public void Session_options_write_both_names_as_the_access_manager_sends_them()
    {
        var camelCase = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var options = JsonSerializer.SerializeToNode(new SessionOptions {
            AllowTcpForwarding = false,
            UdpForwardingBufferSize = new TransferBufferSize(3000, 4000)
        }, camelCase)!;

        Assert.IsFalse(options["allowTcpForwarding"]!.GetValue<bool>());
        Assert.IsFalse(options["allowTcpProxy"]!.GetValue<bool>(), "An older server reads this name.");
        Assert.IsNotNull(options["udpForwardingBufferSize"]);
        Assert.IsNotNull(options["udpProxyBufferSize"], "An older server reads this name.");
    }
}
