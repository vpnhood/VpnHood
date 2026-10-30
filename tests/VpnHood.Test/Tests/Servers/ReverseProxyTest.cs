using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using VpnHood.Core.Client;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.ReverseProxies;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.ProbeResistance;
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
    private static readonly IPAddress ForwardedIp = IPAddress.Parse("1.2.3.4");

    // in the lists below: Cloudflare's, CloudFront's that connect to origins, and one of CloudFront's viewers reach
    private static readonly IPAddress CloudflareIp = IPAddress.Parse("173.245.48.1");
    private static readonly IPAddress CloudFrontIp = IPAddress.Parse("130.176.88.1");
    private static readonly IPAddress CloudFrontIpV6 = IPAddress.Parse("2600:9000:1000::1");
    private static readonly IPAddress CloudFrontViewerIp = IPAddress.Parse("23.228.249.1");

    private const string CloudflareList =
        """{"result":{"ipv4_cidrs":["173.245.48.0/20"],"ipv6_cidrs":["2400:cb00::/32"],"etag":"1"},"success":true,"errors":[],"messages":[]}""";

    private const string CloudFrontList =
        """
        {
          "syncToken": "1",
          "createDate": "2026-10-27-00-00-00",
          "prefixes": [
            { "ip_prefix": "130.176.88.0/21", "region": "GLOBAL", "service": "CLOUDFRONT_ORIGIN_FACING", "network_border_group": "GLOBAL" },
            { "ip_prefix": "23.228.249.0/24", "region": "GLOBAL", "service": "CLOUDFRONT", "network_border_group": "GLOBAL" },
            { "ip_prefix": "3.4.12.4/32", "region": "eu-west-1", "service": "AMAZON", "network_border_group": "eu-west-1" }
          ],
          "ipv6_prefixes": [
            { "ipv6_prefix": "2600:9000:1000::/36", "region": "GLOBAL", "service": "CLOUDFRONT_ORIGIN_FACING", "network_border_group": "GLOBAL" }
          ]
        }
        """;

    [TestMethod]
    public void The_client_is_the_last_entry_outside_the_trusted_ranges()
    {
        using var trust = new ReverseProxyTrust(CreateStoragePath(), httpMessageHandler: null);
        trust.Configure(providers: [], ipRanges: [IpRange.Parse("2.2.2.0/24")]);
        var proxyIp = IPAddress.Parse("2.2.2.1");

        // what the proxy saw, not what its sender wrote before it
        Assert.AreEqual(IPAddress.Parse("1.1.1.1"), trust.GetClientIp(proxyIp, Forwarded("9.9.9.9, 1.1.1.1, 2.2.2.2")));

        // nothing outside them, or nothing readable: the proxy's own address
        Assert.AreEqual(proxyIp, trust.GetClientIp(proxyIp, Forwarded("2.2.2.3")));
        Assert.AreEqual(proxyIp, trust.GetClientIp(proxyIp, Forwarded("bad, 2.2.2.3")));
        Assert.AreEqual(proxyIp, trust.GetClientIp(proxyIp, Forwarded("")));

        // a sender outside them is taken at its own address
        var senderIp = IPAddress.Parse("3.3.3.3");
        Assert.AreEqual(senderIp, trust.GetClientIp(senderIp, Forwarded("1.1.1.1")));
    }

    // edge code on any AWS account may write the header, and CloudFront marks none of it
    [TestMethod]
    public void By_default_Cloudflare_alone_is_trusted()
    {
        CollectionAssert.AreEqual(new[] { ReverseProxyProvider.Cloudflare }, new ServerConfig().ReverseProxyProvidersValue);
    }

    [TestMethod]
    public async Task The_providers_published_addresses_are_trusted_and_kept_for_the_next_start()
    {
        var storagePath = CreateStoragePath();
        ReverseProxyProvider[] providers = [ReverseProxyProvider.Cloudflare, ReverseProxyProvider.CloudFront];

        using var trust = new ReverseProxyTrust(storagePath, new FeedHandler(CloudflareList, CloudFrontList));
        trust.Configure(providers, ipRanges: []);
        await trust.RefreshNow(TestCt);
        Assert.AreEqual(ForwardedIp, trust.GetClientIp(CloudflareIp, Forwarded(ForwardedIp)));
        Assert.AreEqual(ForwardedIp, trust.GetClientIp(CloudFrontIp, Forwarded(ForwardedIp)));
        Assert.AreEqual(ForwardedIp, trust.GetClientIp(CloudFrontIpV6, Forwarded(ForwardedIp)));
        Assert.AreEqual(CloudFrontViewerIp, trust.GetClientIp(CloudFrontViewerIp, Forwarded(ForwardedIp)));

        // the next start can't fetch: the lists the last fetch left stand
        using var nextTrust = new ReverseProxyTrust(storagePath, new FeedHandler(null, null));
        nextTrust.Configure(providers, ipRanges: []);
        await nextTrust.RefreshNow(TestCt);
        Assert.AreEqual(ForwardedIp, nextTrust.GetClientIp(CloudflareIp, Forwarded(ForwardedIp)));
        Assert.AreEqual(ForwardedIp, nextTrust.GetClientIp(CloudFrontIp, Forwarded(ForwardedIp)));
    }

    [TestMethod]
    public async Task A_provider_never_fetched_is_not_trusted()
    {
        using var trust = new ReverseProxyTrust(CreateStoragePath(), new FeedHandler(null, null));
        trust.Configure([ReverseProxyProvider.Cloudflare], [IpRange.Parse("10.0.0.0/8")]);
        await trust.RefreshNow(TestCt);
        Assert.AreEqual(CloudflareIp, trust.GetClientIp(CloudflareIp, Forwarded(ForwardedIp)));

        // the listed ranges still are
        Assert.AreEqual(ForwardedIp, trust.GetClientIp(IPAddress.Parse("10.1.1.1"), Forwarded(ForwardedIp)));
    }

    // anyone may run a Worker on Cloudflare, whose script writes the address headers of its own requests
    [TestMethod]
    public async Task A_cloudflare_worker_request_names_no_client()
    {
        using var trust = new ReverseProxyTrust(CreateStoragePath(), new FeedHandler(CloudflareList, CloudFrontList));
        trust.Configure([ReverseProxyProvider.Cloudflare], ipRanges: []);
        await trust.RefreshNow(TestCt);
        Assert.AreEqual(ForwardedIp, trust.GetClientIp(CloudflareIp, Forwarded(ForwardedIp)));

        var workerHeaders = Forwarded(ForwardedIp);
        workerHeaders["cf-worker"] = "example.com";
        Assert.AreEqual(CloudflareIp, trust.GetClientIp(CloudflareIp, workerHeaders));
    }

    [TestMethod]
    public async Task Without_providers_nothing_is_fetched()
    {
        var feedHandler = new FeedHandler(CloudflareList, CloudFrontList);
        using var trust = new ReverseProxyTrust(CreateStoragePath(), feedHandler);
        trust.Configure(providers: [], ipRanges: []);
        await trust.RefreshNow(TestCt);
        Assert.AreEqual(0, feedHandler.RequestCount);
    }

    // the client IP the hello names is the one the access manager gets; the tests' servers have no provider
    [TestMethod]
    [DataRow(null, "1.2.3.4", "127.0.0.2")] // left out: no proxy listed
    [DataRow("10.0.0.0-10.255.255.255", "1.2.3.4", "127.0.0.2")] // a proxy that is not this sender
    [DataRow("127.0.0.2-127.0.0.2", "1.2.3.4", "1.2.3.4")] // this proxy
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

    // its ports would lead past the proxy, even one no list names
    [TestMethod]
    public async Task A_forwarded_hello_gets_no_udp_or_quic_port()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);

        var directHelloResponse = await SendHello(token, forwardedFor: null);
        Assert.IsNotNull(directHelloResponse.UdpPort);

        var forwardedHelloResponse = await SendHello(token, forwardedFor: ForwardedIp.ToString());
        Assert.IsNull(forwardedHelloResponse.UdpPort);
        Assert.IsNull(forwardedHelloResponse.QuicPort);
    }

    // as the server's parser hands them over: names in any case
    private static Dictionary<string, string> Forwarded(string xForwardedFor)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Forwarded-For"] = xForwardedFor };
    }

    private static Dictionary<string, string> Forwarded(IPAddress ipAddress)
    {
        return Forwarded(ipAddress.ToString());
    }

    private string CreateStoragePath()
    {
        return Path.Combine(TestHelper.WorkingPath, "reverse-proxy", Guid.NewGuid().ToString());
    }

    private async Task<HelloResponse> SendHello(Token token, string? forwardedFor)
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
        var proof = new RequestProof(token.ServerToken.Secret!).Create(
            token.ServerToken.HostName, RequestProof.ProtocolVersion, DateTimeOffset.UtcNow);
        var header =
            $"POST /{Guid.NewGuid()} HTTP/1.1\r\n" +
            $"Host: {token.ServerToken.HostName}\r\n" +
            $"Content-Length: {payload.Length}\r\n" +
            $"X-ProtocolVersion: {VpnHoodClientConfig.MaxProtocolVersion}\r\n" +
            $"Authorization: Bearer {proof}\r\n" +
            "X-BinaryStream: None\r\n" +
            (forwardedFor != null ? $"X-Forwarded-For: {forwardedFor}\r\n" : "") +
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

    // serves the providers' lists from memory; a missing list answers as a provider that is down
    private class FeedHandler(string? cloudflareList, string? cloudFrontList) : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            var list = request.RequestUri?.Host == "api.cloudflare.com" ? cloudflareList : cloudFrontList;
            return Task.FromResult(list != null
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(list) }
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
