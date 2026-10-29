using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Server.ProbeResistance;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Test.Tests.Servers;

// the server's answers to whoever is not a client, as Kestrel gives them behind a JWT bearer check: the parity
// harness of the IP-blocking plan measured them on a real Kestrel, and these keep them from drifting
[TestClass]
public class WebFrontTest : TestBase
{
    [TestMethod]
    public async Task A_request_without_our_headers_gets_kestrels_401_and_the_connection_stays()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        var token = TestHelper.CreateAccessToken(server);
        using var tcpClient = new TcpClient();
        await using var sslStream = await ConnectTls(tcpClient, token);

        // the second request comes on the same connection
        for (var i = 0; i < 2; i++) {
            await sslStream.WriteAsync("GET / HTTP/1.1\r\nHost: x\r\n\r\n"u8.ToArray(), TestCt);
            Assert.AreEqual(
                "HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\nDate: *\r\nServer: Kestrel\r\n" +
                "WWW-Authenticate: Bearer\r\n\r\n",
                WithoutDate(await ReadReplyHead(sslStream)));
        }
    }

    [TestMethod]
    [DataRow("HEAD / HTTP/1.1\r\nHost: x", "HTTP/1.1 401 Unauthorized\r\nDate: *\r\nServer: Kestrel\r\n" +
        "WWW-Authenticate: Bearer\r\n\r\n", true)]
    [DataRow("GET / HTTP/1.0", "HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\nConnection: close\r\n" +
        "Date: *\r\nServer: Kestrel\r\nWWW-Authenticate: Bearer\r\n\r\n", false)]
    [DataRow("GET / HTTP/1.0\r\nConnection: keep-alive", "HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\n" +
        "Connection: keep-alive\r\nDate: *\r\nServer: Kestrel\r\nWWW-Authenticate: Bearer\r\n\r\n", true)]
    [DataRow("GET / HTTP/1.1\r\nHost: x\r\nConnection: Upgrade, close", "HTTP/1.1 401 Unauthorized\r\n" +
        "Content-Length: 0\r\nConnection: close\r\nDate: *\r\nServer: Kestrel\r\nWWW-Authenticate: Bearer\r\n\r\n",
        false)]
    [DataRow("CONNECT x:443 HTTP/1.1\r\nHost: x:443", "HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\n" +
        "Connection: close\r\nDate: *\r\nServer: Kestrel\r\nWWW-Authenticate: Bearer\r\n\r\n", false)]
    public async Task The_401_and_the_connection_follow_the_request(string head, string reply,
        bool keepsConnection)
    {
        await using var stream = new TestDuplexStream(Encoding.ASCII.GetBytes($"{head}\r\n\r\n"));
        var request = await ReadRequest(stream);
        var next = await WebFront.Unauthorized(stream, request, TestCt);

        Assert.AreEqual(reply, WithoutDate(Encoding.ASCII.GetString(stream.Written)));
        Assert.AreEqual(keepsConnection, next == WebFrontNext.NextRequest);
    }

    [TestMethod]
    [DataRow(8190, 0)]
    [DataRow(8191, 414)]
    public async Task The_request_line_takes_8192_bytes_with_its_line_end(int length, int rejection)
    {
        await AssertRead($"GET /{new string('a', length - "GET / HTTP/1.1".Length)} HTTP/1.1\r\nHost: x\r\n\r\n",
            rejection);
    }

    [TestMethod]
    [DataRow(32768, 0)]
    [DataRow(32769, 431)]
    public async Task The_headers_take_32768_bytes_with_their_line_ends(int total, int rejection)
    {
        const string host = "Host: x\r\n";
        var fill = new string('a', total - host.Length - "X-F: \r\n".Length);
        await AssertRead($"GET / HTTP/1.1\r\n{host}X-F: {fill}\r\n\r\n", rejection);
    }

    [TestMethod]
    [DataRow(100, 0)]
    [DataRow(101, 431)]
    public async Task A_request_takes_100_headers(int count, int rejection)
    {
        var headers = string.Concat(Enumerable.Range(1, count - 1).Select(i => $"X-H{i}: v\r\n"));
        await AssertRead($"GET / HTTP/1.1\r\nHost: x\r\n{headers}\r\n", rejection);
    }

    [TestMethod]
    [DataRow("GET / HTTP/2.0", 505)]
    [DataRow("GET / http/1.1", 505)] // a version of HTTP's length
    [DataRow("GET / HTTPS/1.1", 400)]
    [DataRow("GET /", 400)]
    [DataRow("GET  / HTTP/1.1", 400)]
    [DataRow("GET\t/ HTTP/1.1", 400)]
    [DataRow("GET * HTTP/1.1", 405)] // only OPTIONS takes an asterisk
    [DataRow("GET a HTTP/1.1", 405)] // only CONNECT takes an authority
    [DataRow("GET ftp://x/ HTTP/1.1", 400)]
    [DataRow("GET http://other/ HTTP/1.1", 400)] // its host is not the Host header's
    [DataRow("get /a|b{c}^d HTTP/1.1", 0)]
    public async Task A_request_line_follows_kestrels_rules(string requestLine, int rejection)
    {
        await AssertRead($"{requestLine}\r\nHost: x\r\n\r\n", rejection);
    }

    [TestMethod]
    [DataRow("Host: a b", 400)]
    [DataRow("Host: a:x", 400)]
    [DataRow("Host: a\r\nHost: b", 400)]
    [DataRow("Host : x", 400)]
    [DataRow("Host: x\r\nX-A: a\r\n folded", 400)]
    [DataRow("Host: x\r\nX-A: a\rb", 400)]
    [DataRow("Host: x\r\nContent-Length: 5, 5", 400)]
    [DataRow("Host: x\r\nContent-Length: 5\r\nContent-Length: 5", 400)]
    [DataRow("Host: x\r\nTransfer-Encoding: gzip", 400)]
    [DataRow("Host: x\r\nX-A: a\u0001b", 0)]
    [DataRow("Host:", 0)]
    [DataRow("Host: [::1]:443", 0)]
    public async Task Headers_follow_kestrels_rules(string headers, int rejection)
    {
        await AssertRead($"GET / HTTP/1.1\r\n{headers}\r\n\r\n", rejection);
    }

    [TestMethod]
    public async Task A_tls_handshake_where_http_is_expected_gets_400_at_once()
    {
        // a TLS record's start and no line end, which a request would wait for
        await using var stream = new TestDuplexStream([0x16, 0x03, 0x01, 0x00, 0x05], blockAtEnd: true);
        Assert.IsNull(await WebFront.ReadRequest(stream, TestCt));
        StringAssert.StartsWith(Encoding.ASCII.GetString(stream.Written), "HTTP/1.1 400 Bad Request\r\n");
    }

    [TestMethod]
    [DataRow("/a/./b/../c/", "x", 8443, "https://x:8443/a/c/")]
    [DataRow("/../../a", "x", 443, "https://x/a")]
    [DataRow("//a//b", "x", 443, "https://x//a//b")]
    [DataRow("/a%20b%41%7e%2fc%2F?q=%20%41&r=", "x", 8443, "https://x:8443/a%20bA~%2fc%2F?q=%20%41&r=")]
    [DataRow("/a%zz%4", "x", 443, "https://x/a%25zz%254")]
    [DataRow("/caf%C3%A9", "x", 443, "https://x/caf%C3%A9")]
    [DataRow("/a%C3%28", "x", 443, "https://x/a%C3(")]
    [DataRow("/a'b(c)*d!e$f&g+h,i;j=k:l@m[n]", "x", 443, "https://x/a'b(c)*d!e$f&g+h,i;j=k:l@m%5Bn%5D")]
    [DataRow("/a?", "x", 443, "https://x/a?")]
    [DataRow("/a", "example.com:8080", 443, "https://example.com/a")]
    [DataRow("/a", "EXAMPLE.com", 443, "https://EXAMPLE.com/a")]
    [DataRow("/a", "[::1]:80", 8443, "https://[::1]:8443/a")]
    [DataRow("*", "example.com", 443, "https://example.com/")]
    [DataRow("http://example.com/x?y=1", "example.com", 443, "https://example.com/x?y=1")]
    public async Task Port_80_redirects_as_https_redirection_does(string target, string host, int httpsPort,
        string location)
    {
        var method = target == "*" ? "OPTIONS" : "GET";
        await using var stream = new TestDuplexStream(
            Encoding.ASCII.GetBytes($"{method} {target} HTTP/1.1\r\nHost: {host}\r\n\r\n"));
        var request = await ReadRequest(stream);
        Assert.AreEqual(WebFrontNext.NextRequest, await WebFront.RedirectToHttps(stream, request, httpsPort, TestCt));

        var reply = Encoding.ASCII.GetString(stream.Written);
        StringAssert.StartsWith(reply, "HTTP/1.1 307 Temporary Redirect\r\nContent-Length: 0\r\n");
        StringAssert.Contains(reply, $"\r\nServer: Kestrel\r\nLocation: {location}\r\n\r\n");
    }

    [TestMethod]
    [DataRow("Content-Length: 5\r\n\r\nhello")]
    [DataRow("Transfer-Encoding: chunked\r\n\r\n5;a=b\r\nhello\r\n0\r\nX-T: v\r\n\r\n")]
    public async Task A_body_is_dropped_before_the_next_request(string framingAndBody)
    {
        await using var stream = new TestDuplexStream(Encoding.ASCII.GetBytes(
            $"POST / HTTP/1.1\r\nHost: x\r\n{framingAndBody}GET /next HTTP/1.1\r\nHost: x\r\n\r\n"));
        var post = await ReadRequest(stream);
        Assert.AreEqual(WebFrontNext.NextRequest, await WebFront.Unauthorized(stream, post, TestCt));

        var next = await ReadRequest(stream);
        Assert.AreEqual("/next", next.Target);
    }

    [TestMethod]
    public async Task A_body_too_big_to_drop_ends_the_connection()
    {
        await using var stream = new TestDuplexStream(
            "POST / HTTP/1.1\r\nHost: x\r\nContent-Length: 30000001\r\n\r\n"u8.ToArray(), blockAtEnd: true);
        var request = await ReadRequest(stream);
        Assert.AreEqual(WebFrontNext.Close, await WebFront.Unauthorized(stream, request, TestCt));
    }

    // Kestrel drops a body within 5 s and its heartbeat, then resets
    [TestMethod]
    public async Task A_body_that_doesnt_come_resets_the_connection()
    {
        await using var stream = new TestDuplexStream(
            "POST / HTTP/1.1\r\nHost: x\r\nContent-Length: 100\r\n\r\n"u8.ToArray(), blockAtEnd: true);
        var request = await ReadRequest(stream);
        Assert.AreEqual(WebFrontNext.Reset, await WebFront.Unauthorized(stream, request, TestCt));
    }

    [TestMethod]
    public void Above_the_cap_the_busiest_address_loses_its_oldest()
    {
        var connections = new UnauthenticatedConnections { MaxCount = 3 };
        var busiestOldest = new TestConnection();
        var busiestNewer = new TestConnection();
        var other = new TestConnection();
        var newest = new TestConnection();
        connections.Add(EndPoints("10.0.0.1", 1), busiestOldest);
        connections.Add(EndPoints("10.0.0.1", 2), busiestNewer);
        connections.Add(EndPoints("10.0.0.2", 1), other);
        connections.Add(EndPoints("10.0.0.3", 1), newest);

        Assert.IsTrue(busiestOldest.IsDisposed);
        Assert.IsFalse(busiestNewer.IsDisposed || other.IsDisposed || newest.IsDisposed);
        Assert.AreEqual(3, connections.Count);
    }

    // a /64 is one address, as one machine holds it all
    [TestMethod]
    public void An_ipv6_address_counts_by_its_64()
    {
        var connections = new UnauthenticatedConnections { MaxCount = 2 };
        var first = new TestConnection();
        connections.Add(EndPoints("2001:db8::1", 1), first);
        connections.Add(EndPoints("2001:db8::2", 1), new TestConnection());
        connections.Add(EndPoints("2001:db9::1", 1), new TestConnection());

        Assert.IsTrue(first.IsDisposed);
    }

    [TestMethod]
    public void A_proven_connection_leaves_the_count()
    {
        var connections = new UnauthenticatedConnections { MaxCount = 1 };
        var proven = new TestConnection();
        connections.Add(EndPoints("10.0.0.1", 1), proven);
        connections.Remove(EndPoints("10.0.0.1", 1));
        connections.Add(EndPoints("10.0.0.1", 2), new TestConnection());

        Assert.IsFalse(proven.IsDisposed);
        Assert.AreEqual(1, connections.Count);
    }

    [TestMethod]
    public async Task The_cap_closes_the_server_s_oldest_connection_of_the_busiest_address()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.ServerConfig.SessionOptions.MaxUnauthenticatedConnectionCount = 2;
        await using var server = await TestHelper.CreateServer(accessManager);
        var serverEndPoint = server.ServerHost.TcpEndPoints.First();

        // TCP alone: the TLS handshake would wait 10 s for a hello, so a sooner close is the cap's
        using var oldest = new TcpClient();
        await oldest.ConnectAsync(serverEndPoint, TestCt);
        await VhTestUtil.AssertEqualsWait(1, () => server.UnauthenticatedConnectionCount, cancellationToken: TestCt);
        using var newer = new TcpClient();
        await newer.ConnectAsync(serverEndPoint, TestCt);
        await VhTestUtil.AssertEqualsWait(2, () => server.UnauthenticatedConnectionCount, cancellationToken: TestCt);
        using var newest = new TcpClient();
        await newest.ConnectAsync(serverEndPoint, TestCt);

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var read = await oldest.GetStream().ReadAsync(new byte[1], readCts.Token);
        Assert.AreEqual(0, read);
        Assert.AreEqual(2, server.UnauthenticatedConnectionCount);
    }

    [TestMethod]
    public async Task A_client_s_connections_leave_the_count()
    {
        using var accessManager = TestHelper.CreateAccessManager();
        await using var server = await TestHelper.CreateServer(accessManager);
        await using var client = await TestHelper.CreateClient(TestHelper.CreateAccessToken(server));

        await VhTestUtil.AssertEqualsWait(0, () => server.UnauthenticatedConnectionCount, cancellationToken: TestCt);
    }

    private async Task AssertRead(string request, int rejection)
    {
        await using var stream = new TestDuplexStream(Encoding.Latin1.GetBytes(request));
        var head = await WebFront.ReadRequest(stream, TestCt);
        if (rejection == 0) {
            Assert.IsNotNull(head, Encoding.ASCII.GetString(stream.Written));
            return;
        }

        Assert.IsNull(head);
        var reply = Encoding.ASCII.GetString(stream.Written);
        StringAssert.StartsWith(reply, $"HTTP/1.1 {rejection} ");
        StringAssert.Contains(reply, "\r\nContent-Length: 0\r\nConnection: close\r\n");
    }

    private async Task<HttpRequestHead> ReadRequest(Stream stream)
    {
        return await WebFront.ReadRequest(stream, TestCt) ??
               throw new AssertFailedException("The request was refused.");
    }

    private static string WithoutDate(string reply)
    {
        return Regex.Replace(reply, "\r\nDate: [^\r]*\r\n", "\r\nDate: *\r\n");
    }

    private static IPEndPointPair EndPoints(string remoteAddress, int remotePort)
    {
        return new IPEndPointPair(new IPEndPoint(IPAddress.Loopback, 443),
            new IPEndPoint(IPAddress.Parse(remoteAddress), remotePort));
    }

    private async Task<SslStream> ConnectTls(TcpClient tcpClient, Token token)
    {
        var serverEndPoint = token.ServerToken.HostEndPoints?.First()
                             ?? throw new InvalidOperationException("The token has no endpoint.");
        await tcpClient.ConnectAsync(serverEndPoint, TestCt);
        var sslStream = new SslStream(tcpClient.GetStream());
        await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
            TargetHost = token.ServerToken.HostName,
            RemoteCertificateValidationCallback = (_, _, _, _) => true
        }, TestCt);
        return sslStream;
    }

    private async Task<string> ReadReplyHead(Stream stream)
    {
        var head = new StringBuilder();
        var buffer = new byte[1];
        while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal)) {
            if (await stream.ReadAsync(buffer, TestCt) == 0)
                throw new EndOfStreamException("The connection closed before the reply's end.");
            head.Append((char)buffer[0]);
        }

        return head.ToString();
    }

    private sealed class TestConnection : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }

    // reads the bytes it was given, then ends, or waits as an open connection does; keeps what is written
    private sealed class TestDuplexStream(byte[] input, bool blockAtEnd = false) : Stream
    {
        private readonly MemoryStream _input = new(input);
        private readonly MemoryStream _output = new();

        public byte[] Written => _output.ToArray();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();

        public override long Position {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = _input.Read(buffer.Span);
            if (read == 0 && blockAtEnd)
                await Task.Delay(Timeout.Infinite, cancellationToken);
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _output.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override void Write(byte[] buffer, int offset, int count) => _output.Write(buffer, offset, count);
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
