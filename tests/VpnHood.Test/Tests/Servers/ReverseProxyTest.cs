using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using VpnHood.Core.Client;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Server.Utils;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Streams;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Test.Tests.Servers;

// The hello is written by hand, as a proxy passes one on: from 127.0.0.2, with the X-Forwarded-For it carries
[TestClass]
public class ReverseProxyTest : TestBase
{
    private static readonly IPAddress ProxyIp = IPAddress.Parse("127.0.0.2");

    [TestMethod]
    public void The_forwarded_client_ip_follows_the_reverse_proxy_ranges()
    {
        // left out: the first entry, the sender's own word
        Assert.AreEqual(IPAddress.Parse("1.1.1.1"),
            ServerUtils.GetClientIpFromXForwarded("1.1.1.1, 2.2.2.2", reverseProxyIpRanges: null));

        // set: the last entry outside them, as the proxy appended what it saw to what the sender wrote
        var reverseProxyIpRanges = new IpRangeOrderedList([IpRange.Parse("2.2.2.0-2.2.2.255")]);
        Assert.AreEqual(IPAddress.Parse("1.1.1.1"),
            ServerUtils.GetClientIpFromXForwarded("9.9.9.9, 1.1.1.1, 2.2.2.2", reverseProxyIpRanges));
        Assert.IsNull(ServerUtils.GetClientIpFromXForwarded("2.2.2.1", reverseProxyIpRanges));
        Assert.IsNull(ServerUtils.GetClientIpFromXForwarded("bad, 2.2.2.1", reverseProxyIpRanges));
        Assert.IsNull(ServerUtils.GetClientIpFromXForwarded("", reverseProxyIpRanges));
    }

    // the client IP the hello names is the one the access manager gets
    [TestMethod]
    [DataRow(null, "1.2.3.4", "1.2.3.4")] // left out: the header counts from anyone
    [DataRow("", "1.2.3.4", "127.0.0.2")] // []: from no one
    [DataRow("10.0.0.0-10.255.255.255", "1.2.3.4", "127.0.0.2")] // from a proxy that is not this sender
    [DataRow("127.0.0.2-127.0.0.2", "1.2.3.4", "1.2.3.4")] // from this proxy
    [DataRow("127.0.0.2-127.0.0.2", "1.2.3.4, 127.0.0.9", "127.0.0.9")] // what the proxy saw, not what its sender wrote
    public async Task The_client_ip_follows_the_reverse_proxy_ranges(string? reverseProxyIpRanges,
        string forwardedFor, string expectedClientIp)
    {
        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.ServerConfig.ReverseProxyIpRanges = reverseProxyIpRanges?
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(IpRange.Parse)
            .ToArray();
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);

        var helloResponse = await SendHello(token, forwardedFor);
        Assert.AreEqual(IPAddress.Parse(expectedClientIp), helloResponse.ClientPublicAddress);
    }

    private async Task<HelloResponse> SendHello(Token token, string forwardedFor)
    {
        var serverEndPoint = token.ServerToken.HostEndPoints?.First(x => x.AddressFamily == ProxyIp.AddressFamily)
                             ?? throw new InvalidOperationException("The token has no endpoint.");

        using var tcpClient = new TcpClient(new IPEndPoint(ProxyIp, 0));
        await tcpClient.ConnectAsync(serverEndPoint, TestCt);
        await using var sslStream = new SslStream(tcpClient.GetStream());
        await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
            TargetHost = token.ServerToken.HostName,
            RemoteCertificateValidationCallback = (_, _, _, _) => true
        }, TestCt);

        var payload = BuildHelloPayload(token);
        var header =
            $"POST /{Guid.NewGuid()} HTTP/1.1\r\n" +
            $"Host: {token.ServerToken.HostName}\r\n" +
            $"Content-Length: {payload.Length}\r\n" +
            $"X-ProtocolVersion: {VpnHoodClientConfig.MaxProtocolVersion}\r\n" +
            "X-BinaryStream: None\r\n" +
            $"X-Forwarded-For: {forwardedFor}\r\n" +
            "\r\n";
        await sslStream.WriteAsync(Encoding.UTF8.GetBytes(header), TestCt);
        await sslStream.WriteAsync(payload, TestCt);
        await sslStream.FlushAsync(TestCt);

        var response = await HttpUtils.ReadResponse(sslStream, TestCt);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return await StreamUtils.ReadObjectAsync<HelloResponse>(sslStream, TestCt);
    }

    // as the client writes a request: its version, its code, then its JSON
    private static byte[] BuildHelloPayload(Token token)
    {
        var clientInfo = new ClientInfo {
            ClientId = Guid.NewGuid().ToString(),
            ClientVersion = "1.0.0",
            MinProtocolVersion = VpnHoodClientConfig.MinProtocolVersion,
            MaxProtocolVersion = VpnHoodClientConfig.MaxProtocolVersion,
            UserAgent = "Test"
        };
        var request = new HelloRequest {
            RequestId = UniqueIdFactory.Create(),
            EncryptedClientId = VhUtils.EncryptClientId(clientInfo.ClientId, token.Secret),
            ClientInfo = clientInfo,
            TokenId = token.TokenId
        };

        var requestBuffer = StreamUtils.ObjectToJsonBuffer(request);
        var payload = new byte[2 + requestBuffer.Length];
        payload[0] = 1;
        payload[1] = request.RequestCode;
        requestBuffer.Span.CopyTo(payload.AsSpan(2));
        return payload;
    }
}
