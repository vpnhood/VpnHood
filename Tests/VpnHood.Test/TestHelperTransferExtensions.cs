using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Test.QuicTesters;

namespace VpnHood.Test;

public static class TestHelperTransferExtensions
{
    extension(TestHelper testHelper)
    {
        public async Task Test_Ping(Ping? ping = null, IPAddress? ipAddress = null, TimeSpan? timeout = null)
        {
            using var pingTmp = new Ping();
            ipAddress ??= testHelper.WebServer.MockEps.PingV4Address1;
            timeout ??= TestConstants.DefaultHttpTimeout;
            ping ??= pingTmp;
            var pingReply = await ping.SendPingAsync(ipAddress, timeout.Value, new byte[1024]);
            if (pingReply.Status != IPStatus.Success)
                throw new PingException($"Ping failed. Status: {pingReply.Status}");
        }

        public async Task Test_Dns(IPEndPoint? nsEndPoint = null, TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            timeout ??= TimeSpan.FromSeconds(3);
            var hostEntry = await DnsResolver.GetHostEntry("www.google.com",
                nsEndPoint ?? testHelper.WebServer.MockEps.UdpV4EndPoint1,
                timeout.Value, cancellationToken);
            Assert.IsNotNull(hostEntry);
            Assert.IsNotEmpty(hostEntry.AddressList);
        }

        public async Task Test_UdpEcho(IPEndPoint? udpEndPoint = null, TimeSpan? timeout = null)
        {
            udpEndPoint ??= testHelper.WebServer.MockEps.UdpV4EndPoint1;
            if (udpEndPoint.IsV4()) {
                using var udpClient = new UdpClient(AddressFamily.InterNetwork);
                await testHelper.Test_UdpEcho(udpClient, udpEndPoint, timeout);
            }
            else if (udpEndPoint.IsV6()) {
                using var udpClient = new UdpClient(AddressFamily.InterNetworkV6);
                await testHelper.Test_UdpEcho(udpClient, udpEndPoint, timeout);
            }
        }

        public async Task Test_UdpEcho(UdpClient udpClient, IPEndPoint? udpEndPoint = null, TimeSpan? timeout = null)
        {
            timeout ??= TestConstants.DefaultUdpTimeout;
            udpEndPoint ??= testHelper.WebServer.MockEps.UdpV4EndPoint1;
            var buffer = new byte[1024];
            new Random().NextBytes(buffer);
            var sentBytes = await udpClient.SendAsync(buffer, udpEndPoint, new CancellationTokenSource(timeout.Value).Token);
            Assert.AreEqual(buffer.Length, sentBytes);
            using var cts = new CancellationTokenSource(timeout.Value);
            var res = await udpClient.ReceiveAsync(cts.Token);
            CollectionAssert.AreEquivalent(buffer, res.Buffer);
        }

        public async Task Test_UdpByDNS(IPEndPoint udpEndPoint, TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            timeout ??= TestConstants.DefaultUdpTimeout;
            var result = await DnsResolver.GetHostEntry("www.google.com", udpEndPoint, timeout.Value, cancellationToken);
            Assert.IsNotEmpty(result.AddressList);
        }

        public async Task Test_UdpUpload(int byteCount = 10_000, IPEndPoint? udpEndPoint = null, TimeSpan? timeout = null)
        {
            udpEndPoint ??= testHelper.WebServer.MockEps.UdpUploadEndPoint1;
            timeout ??= TestConstants.DefaultUdpTimeout;

            var data = new byte[byteCount];
            Random.Shared.NextBytes(data);

            uint expectedChecksum = 0;
            foreach (var b in data)
                expectedChecksum += b;

            // Datagram: [4-byte length (big-endian)][data]
            var datagram = new byte[4 + byteCount];
            datagram[0] = (byte)(byteCount >> 24);
            datagram[1] = (byte)(byteCount >> 16);
            datagram[2] = (byte)(byteCount >> 8);
            datagram[3] = (byte)byteCount;
            data.CopyTo(datagram, 4);

            using var udpClient = new UdpClient(udpEndPoint.AddressFamily);
            using var cts = new CancellationTokenSource(timeout.Value);
            await udpClient.SendAsync(datagram, udpEndPoint, cts.Token);

            var result = await udpClient.ReceiveAsync(cts.Token);
            var buf = result.Buffer;
            var actualChecksum = (uint)(buf[0] << 24 | buf[1] << 16 | buf[2] << 8 | buf[3]);
            Assert.AreEqual(expectedChecksum, actualChecksum, "UdpUpload checksum mismatch.");
        }

        public async Task Test_UdpDownload(int byteCount = 10_000, IPEndPoint? udpEndPoint = null, TimeSpan? timeout = null)
        {
            udpEndPoint ??= testHelper.WebServer.MockEps.UdpDownloadEndPoint1;
            timeout ??= TestConstants.DefaultUdpTimeout;

            // Datagram: [4-byte length (big-endian)]
            var request = new[] {
                (byte)(byteCount >> 24), (byte)(byteCount >> 16),
                (byte)(byteCount >> 8), (byte)byteCount
            };

            using var udpClient = new UdpClient(udpEndPoint.AddressFamily);
            using var cts = new CancellationTokenSource(timeout.Value);
            await udpClient.SendAsync(request, udpEndPoint, cts.Token);

            var result = await udpClient.ReceiveAsync(cts.Token);
            var buf = result.Buffer;

            // Response: [data (byteCount bytes)][4-byte checksum]
            Assert.AreEqual(byteCount + 4, buf.Length, "UdpDownload response length mismatch.");
            uint receivedChecksum = (uint)(buf[byteCount] << 24 | buf[byteCount + 1] << 16 |
                                           buf[byteCount + 2] << 8 | buf[byteCount + 3]);
            uint actualChecksum = 0;
            for (var i = 0; i < byteCount; i++)
                actualChecksum += buf[i];
            Assert.AreEqual(receivedChecksum, actualChecksum, "UdpDownload checksum mismatch.");
        }

        public async Task<bool> Test_QuicEcho(Uri? uri = null, TimeSpan? timeout = null, bool throwError = true)
        {
            uri ??= testHelper.WebServer.MockEps.QuicUrl1;
            IPEndPoint? ipEndPoint = null;
            if (uri.Equals(testHelper.WebServer.MockEps.QuicUrl1))
                ipEndPoint = testHelper.TestIps.MapToRemote(testHelper.WebServer.LocalEps.QuicEndPoint1);
            if (uri.Equals(testHelper.WebServer.MockEps.QuicUrl2))
                ipEndPoint = testHelper.TestIps.MapToRemote(testHelper.WebServer.LocalEps.QuicEndPoint2);
            ipEndPoint ??= new IPEndPoint(IPAddress.Parse(uri.Host), uri.Port);
            try {
                VhLogger.Instance.LogInformation(GeneralEventId.Test, "Testing a QUIC uri. Url: {uri}", uri);
                return await testHelper.Test_QuicEcho(uri.Host, ipEndPoint, timeout, throwError);
            }
            catch when (!throwError) {
                return false;
            }
        }

        public async Task<bool> Test_QuicEcho(string domain, IPEndPoint ipEndPoint, TimeSpan? timeout = null,
            bool throwError = true)
        {
            timeout ??= TestConstants.DefaultQuicTimeout;
            var quicTesterClient = new QuicTesterClient(ipEndPoint, domain, timeout);
            var data = new byte[1024 * 1024 * 10];
            Random.Shared.NextBytes(data);
            try {
                var response = await quicTesterClient.SendAndReceive(data, CancellationToken.None);
                if (!throwError)
                    return data.SequenceEqual(response);
                CollectionAssert.AreEquivalent(data, response);
                return true;
            }
            catch when (!throwError) {
                return false;
            }
        }

        public async Task<bool> Test_Https(Uri? uri = null, TimeSpan? timeout = null, bool throwError = true)
        {
            uri ??= testHelper.WebServer.MockEps.HttpsUrl1;
            IPAddress? ipAddress = null;
            if (uri.Equals(testHelper.WebServer.MockEps.HttpsUrl1))
                ipAddress = testHelper.TestIps.MapToRemote(IPAddress.Parse(testHelper.WebServer.LocalEps.HttpsUrl1.Host));
            if (uri.Equals(testHelper.WebServer.MockEps.HttpsUrl2))
                ipAddress = testHelper.TestIps.MapToRemote(IPAddress.Parse(testHelper.WebServer.LocalEps.HttpsUrl2.Host));
            try {
                VhLogger.Instance.LogInformation(GeneralEventId.Test, "Fetching a test uri. Url: {uri}", uri);
                Assert.IsTrue(await SendHttpGet(testHelper, uri, ipAddress, timeout),
                    $"Could not fetch the test uri: {uri}");
                return true;
            }
            catch when (!throwError) {
                return false;
            }
        }

        public async Task Test_TcpUpload(int byteCount = 50_000,
            IPEndPoint? tcpEndPoint = null, TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            tcpEndPoint ??= testHelper.WebServer.MockEps.TcpUploadEndPoint1;
            timeout ??= TestConstants.DefaultHttpTimeout;

            var data = new byte[byteCount];
            Random.Shared.NextBytes(data);

            uint expectedChecksum = 0;
            foreach (var b in data)
                expectedChecksum += b;

            using var cts = new CancellationTokenSource(timeout.Value);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(tcpEndPoint, linkedCts.Token);
            var stream = tcpClient.GetStream();

            var lenBuf = new[] {
                (byte)(byteCount >> 24), (byte)(byteCount >> 16),
                (byte)(byteCount >> 8), (byte)byteCount
            };
            await stream.WriteAsync(lenBuf, linkedCts.Token);
            await stream.WriteAsync(data, linkedCts.Token);

            var resultBuf = new byte[4];
            await stream.ReadExactlyAsync(resultBuf, linkedCts.Token);
            var actualChecksum = (uint)(resultBuf[0] << 24 | resultBuf[1] << 16 | resultBuf[2] << 8 | resultBuf[3]);

            Assert.AreEqual(expectedChecksum, actualChecksum, "TcpUpload checksum mismatch.");
        }

        public async Task Test_TcpDownload(int byteCount = 50_000,
            IPEndPoint? tcpEndPoint = null, TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            tcpEndPoint ??= testHelper.WebServer.MockEps.TcpDownloadEndPoint1;
            timeout ??= TestConstants.DefaultHttpTimeout;

            using var cts = new CancellationTokenSource(timeout.Value);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(tcpEndPoint, linkedCts.Token);
            var stream = tcpClient.GetStream();

            var lenBuf = new[] {
                (byte)(byteCount >> 24), (byte)(byteCount >> 16),
                (byte)(byteCount >> 8), (byte)byteCount
            };
            await stream.WriteAsync(lenBuf, linkedCts.Token);

            var data = new byte[byteCount];
            await stream.ReadExactlyAsync(data, linkedCts.Token);

            var resultBuf = new byte[4];
            await stream.ReadExactlyAsync(resultBuf, linkedCts.Token);
            var receivedChecksum = (uint)(resultBuf[0] << 24 | resultBuf[1] << 16 | resultBuf[2] << 8 | resultBuf[3]);

            uint actualChecksum = 0;
            foreach (var b in data)
                actualChecksum += b;

            Assert.AreEqual(receivedChecksum, actualChecksum, "TcpDownload checksum mismatch.");
        }
    }

    private static async Task<bool> SendHttpGet(TestHelper testHelper, Uri uri, IPAddress? ipAddress,
        TimeSpan? timeout = null)
    {
        using var handler = new SocketsHttpHandler();
        handler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions {
            RemoteCertificateValidationCallback = (_, _, _, _) => true,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        };
        if (ipAddress != null) {
            handler.ConnectCallback = async (_, cancellationToken) => {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try {
                    await socket.ConnectAsync(new IPEndPoint(ipAddress, uri.Port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch {
                    socket.Dispose();
                    throw;
                }
            };
        }

        // ReSharper disable once ShortLivedHttpClient
        using var httpClient = new HttpClient(handler);
        timeout ??= TestConstants.DefaultHttpTimeout;
        using var cts = new CancellationTokenSource(timeout.Value);
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, uri);
        if (IPEndPoint.TryParse(requestMessage.RequestUri!.Authority, out var ipEndPoint) &&
            testHelper.ClientNetFilter.IpMapper?.ToHost(IpProtocol.Tcp, ipEndPoint.ToValue(), out var newEndPoint) == true)
            requestMessage.Headers.Host = newEndPoint.Address.ToString();
        var response = await httpClient.SendAsync(requestMessage, cts.Token);
        var res = await response.Content.ReadAsStringAsync(cts.Token);
        return res.Length > 100;
    }
}

