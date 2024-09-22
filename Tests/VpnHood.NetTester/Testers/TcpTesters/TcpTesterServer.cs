using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.NetTester.Utils;

namespace VpnHood.NetTester.Testers.TcpTesters;

public class TcpTesterServer : IDisposable
{
    private TcpListener? _tcpListener;

    public IPEndPoint ListenerEndPoint =>
        (IPEndPoint?)_tcpListener?.LocalEndpoint ?? throw new InvalidOperationException("Server has not been started.");

    public async Task Start(IPEndPoint endPoint, CancellationToken cancellationToken)
    {
        if (_tcpListener != null)
            throw new InvalidOperationException("Server is already running.");

        _tcpListener = new TcpListener(endPoint);
        _tcpListener.Start();
        while (!cancellationToken.IsCancellationRequested) {
            var client = await _tcpListener.AcceptTcpClientAsync(cancellationToken);
            _ = ProcessClient(client, cancellationToken);
        }

        _tcpListener = null;
    }

    private static async Task ProcessClient(TcpClient client, CancellationToken cancellationToken)
    {
        var remoteEndPoint = client.Client.RemoteEndPoint;
        VhLogger.Instance.LogInformation("Server: Start processing client. ClientEp: {ClientEp}", remoteEndPoint);
        try {
            await using var stream = client.GetStream();

            // find read size
            var buffer = new byte[16];
            await stream.ReadAtLeastAsync(buffer, 16, true, cancellationToken);

            var readSize = BitConverter.ToInt64(buffer, 0);
            var writeSize = BitConverter.ToInt64(buffer, 8);

            // read data
            await using var discarder = new StreamDiscarder(null);
            await discarder.ReadFromAsync(stream, size: readSize, cancellationToken: cancellationToken);

            // write data
            await using var randomReader = new StreamRandomReader(writeSize, null);
            await randomReader.CopyToAsync(stream, cancellationToken);
        }
        finally {
            VhLogger.Instance.LogInformation("Server: Finish processing client. ClientEp: {ClientEp}", remoteEndPoint);
            client.Dispose();
        }
    }

    public void Dispose()
    {
        _tcpListener?.Stop();
    }
}