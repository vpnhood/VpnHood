using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Net.Toolkit.Streams;
using VpnHood.Core.Server.Access;

namespace VpnHood.Test.Tests;

// These are measurements of the local SslStream implementation, not a promise about another OS's TLS
// record layout. The same test can be run on Windows, Linux and macOS and its output compared.
[TestClass]
public class WebSocketPaddingTlsTest
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Measure_outer_TLS_records_on_new_and_reused_connections(bool padding)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        var ct = cts.Token;
        using var certificate = CertificateUtil.CreateSelfSigned();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var clientSocket = new TcpClient();
        try {
            var accept = listener.AcceptTcpClientAsync(ct).AsTask();
            await clientSocket.ConnectAsync((IPEndPoint)listener.LocalEndpoint, ct);
            using var serverSocket = await accept;
            await using var clientTap = new TlsRecordingStream(clientSocket.GetStream());
            await using var serverTap = new TlsRecordingStream(serverSocket.GetStream());
            await using var clientTls = new SslStream(clientTap, leaveInnerStreamOpen: true, (_, _, _, _) => true);
            await using var serverTls = new SslStream(serverTap, leaveInnerStreamOpen: true);
            await Task.WhenAll(
                clientTls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
                    TargetHost = "localhost",
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }, ct),
                serverTls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
                    ServerCertificate = certificate,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }, ct));

            TestContext.WriteLine($"OS={RuntimeInformation.OSDescription}; Runtime={RuntimeInformation.FrameworkDescription}; " +
                                  $"TLS={clientTls.SslProtocol}; Padding={padding}");
            var client = new WebSocketStream(clientTls, "tls-client", false, false, useStandardMasking: true);
            var server = new WebSocketStream(serverTls, "tls-server", false, true, useStandardMasking: true);
            var totalWireBytes = 0;
            // A small page-load sample: two directions on eight logical streams sharing one TLS connection.
            for (var i = 0; i < 8; i++) {
                if (padding) {
                    client.EnableInitialPadding();
                    server.EnableInitialPadding();
                }
                clientTap.Clear();
                serverTap.Clear();
                var hello = new byte[517];
                Random.Shared.NextBytes(hello);
                await client.WriteAsync(hello, ct);
                var requestRecords = clientTap.RecordLengths();
                var received = new byte[hello.Length];
                await server.ReadExactlyAsync(received, ct);
                CollectionAssert.AreEqual(hello, received);

                var reply = new byte[83];
                Random.Shared.NextBytes(reply);
                await server.WriteAsync(reply, ct);
                var responseRecords = serverTap.RecordLengths();
                var receivedReply = new byte[reply.Length];
                await client.ReadExactlyAsync(receivedReply, ct);
                CollectionAssert.AreEqual(reply, receivedReply);

                Assert.IsTrue(requestRecords.Length > 0 && responseRecords.Length > 0);
                if (padding) {
                    Assert.IsTrue(requestRecords[0] > hello.Length + 128,
                        "Padding must change the first outer record, not only append another record.");
                    Assert.IsTrue(responseRecords[0] > reply.Length + 128);
                }
                TestContext.WriteLine($"Stream={i}; Reused={i != 0}; Client record payloads=[{string.Join(",", requestRecords)}]; " +
                                      $"Server record payloads=[{string.Join(",", responseRecords)}]");

                // Draining both ends also proves that ignored text frames and the logical terminators leave
                // the next stream exactly at its first frame, including when a gap timer races disposal.
                await Task.WhenAll(client.DisposeAsync().AsTask(), server.DisposeAsync().AsTask()).WaitAsync(ct);
                totalWireBytes += clientTap.RecordLengths().Sum(x => x + 5) + serverTap.RecordLengths().Sum(x => x + 5);
                if (i < 7) {
                    client = (WebSocketStream)await client.CreateReuse();
                    server = (WebSocketStream)await server.CreateReuse();
                }
            }
            TestContext.WriteLine($"Eight logical streams: real payload=4800 bytes; outer TLS wire={totalWireBytes} bytes; " +
                                  "includes framing, authentication tags, stream terminators and any gap cover.");
        }
        finally {
            listener.Stop();
        }
    }

    private sealed class TlsRecordingStream(Stream source) : StreamDecoratorAsync(source, leaveOpen: true)
    {
        private readonly Lock _lock = new();
        private readonly List<byte> _bytes = [];

        public void Clear()
        {
            lock (_lock)
                _bytes.Clear();
        }

        public int[] RecordLengths()
        {
            byte[] bytes;
            lock (_lock)
                bytes = _bytes.ToArray();
            var result = new List<int>();
            for (var offset = 0; offset < bytes.Length;) {
                Assert.IsTrue(bytes.Length - offset >= 5, "Incomplete TLS record header.");
                var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 3, 2));
                Assert.IsTrue(offset + 5 + length <= bytes.Length, "Incomplete TLS record payload.");
                if (bytes[offset] == 23)
                    result.Add(length);
                offset += 5 + length;
            }
            return result.ToArray();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            lock (_lock)
                _bytes.AddRange(buffer.ToArray());
            await SourceStream.WriteAsync(buffer, cancellationToken);
        }
    }
}
