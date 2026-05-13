using System.Net;
using System.Net.Sockets;

namespace VpnHood.Test;

public static class TestHelperTransferExtensions
{
    extension(TestHelper testHelper)
    {
        public async Task Test_TcpUpload(int byteCount,
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

            // Send 4-byte length prefix (big-endian) followed by data
            var lenBuf = new[] {
                (byte)(byteCount >> 24), (byte)(byteCount >> 16),
                (byte)(byteCount >> 8), (byte)byteCount
            };
            await stream.WriteAsync(lenBuf, linkedCts.Token);
            await stream.WriteAsync(data, linkedCts.Token);

            // Read back 4-byte checksum (big-endian)
            var resultBuf = new byte[4];
            await stream.ReadExactlyAsync(resultBuf, linkedCts.Token);
            var actualChecksum = (uint)(resultBuf[0] << 24 | resultBuf[1] << 16 | resultBuf[2] << 8 | resultBuf[3]);

            Assert.AreEqual(expectedChecksum, actualChecksum, "TcpUpload checksum mismatch.");
        }

        public async Task Test_TcpDownload(int byteCount,
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

            // Send 4-byte length prefix (big-endian)
            var lenBuf = new[] {
                (byte)(byteCount >> 24), (byte)(byteCount >> 16),
                (byte)(byteCount >> 8), (byte)byteCount
            };
            await stream.WriteAsync(lenBuf, linkedCts.Token);

            // Read exactly byteCount bytes of data
            var data = new byte[byteCount];
            await stream.ReadExactlyAsync(data, linkedCts.Token);

            // Read back 4-byte checksum (big-endian)
            var resultBuf = new byte[4];
            await stream.ReadExactlyAsync(resultBuf, linkedCts.Token);
            var receivedChecksum = (uint)(resultBuf[0] << 24 | resultBuf[1] << 16 | resultBuf[2] << 8 | resultBuf[3]);

            uint actualChecksum = 0;
            foreach (var b in data)
                actualChecksum += b;

            Assert.AreEqual(receivedChecksum, actualChecksum, "TcpDownload checksum mismatch.");
        }
    }
}
