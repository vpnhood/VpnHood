using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Client;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Server.Access;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

// ALPN as a Kestrel with HTTP/2 off answers it and an HTTP library offers it
[TestClass]
public class AlpnTest : TestBase
{
    [TestMethod]
    public async Task Server_picks_http11_from_a_browser_offer()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        var alpn = await Negotiate(token, [SslApplicationProtocol.Http2, SslApplicationProtocol.Http11]);
        Assert.AreEqual(SslApplicationProtocol.Http11, alpn);
    }

    // older clients offer none
    [TestMethod]
    public async Task Server_takes_a_hello_without_alpn()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        var alpn = await Negotiate(token, offer: null);
        Assert.AreEqual(default(SslApplicationProtocol), alpn);
    }

    [TestMethod]
    public async Task Server_fails_the_handshake_of_an_h2_only_hello()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        await Assert.ThrowsExactlyAsync<AuthenticationException>(() =>
            Negotiate(token, [SslApplicationProtocol.Http2]));
    }

    [TestMethod]
    public async Task Client_offers_http11_alone()
    {
        using var certificate = CertificateUtil.CreateSelfSigned();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        // a server that takes h2 whenever it is offered
        var serverAlpnTask = Accept(listener, certificate,
            [SslApplicationProtocol.Http2, SslApplicationProtocol.Http11]);

        var vpnEndPoint = new VpnEndPoint((IPEndPoint)listener.LocalEndpoint, "localhost",
            CertificateHash: null, PathBase: null);
        using var factory = new TcpStreamConnectionFactory(new TestSocketFactory(), proxyConnector: null,
            vpnEndPoint, (_, _, _, _) => true, tcpPacketChannelKernelBufferSize: null);
        await using var connection = await factory.CreateConnection("alpn-test", isTcpPacketChannel: false,
            onConnectAttempt: null, TestCt);

        Assert.AreEqual(SslApplicationProtocol.Http11, await serverAlpnTask);
    }

    // the protocol the test server picks from this offer
    private async Task<SslApplicationProtocol> Negotiate(Token token, IReadOnlyList<SslApplicationProtocol>? offer)
    {
        var hostEndPoint = token.ServerToken.HostEndPoints?.First() ??
                           throw new InvalidOperationException("The token has no endpoint.");

        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(hostEndPoint, TestCt);
        await using var sslStream = new SslStream(tcpClient.GetStream());
        await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
            TargetHost = token.ServerToken.HostName,
            RemoteCertificateValidationCallback = (_, _, _, _) => true,
            ApplicationProtocols = offer?.ToList()
        }, TestCt);

        return sslStream.NegotiatedApplicationProtocol;
    }

    // the protocol a server with this list picks for the next client
    private async Task<SslApplicationProtocol> Accept(TcpListener listener, X509Certificate2 certificate,
        IReadOnlyList<SslApplicationProtocol> protocols)
    {
        using var tcpClient = await listener.AcceptTcpClientAsync(TestCt);
        await using var sslStream = new SslStream(tcpClient.GetStream());
        await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
            ServerCertificate = certificate,
            ApplicationProtocols = protocols.ToList()
        }, TestCt);

        return sslStream.NegotiatedApplicationProtocol;
    }
}
