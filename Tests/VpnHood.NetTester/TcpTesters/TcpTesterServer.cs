using System.Net;
using System.Net.Sockets;

namespace VpnHood.NetTester.TcpTesters;

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
        Console.WriteLine($"Start processing client. ClientEp: {remoteEndPoint}");
        try {
            await using var stream = client.GetStream();

            // read int data from stream
            var buffer = new byte[8];
            if (await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken) < 4)
                return;

            var byteCount = BitConverter.ToInt64(buffer, 0);
            await TcpTesterUtil.ReadData(stream, byteCount, speedometer: null, cancellationToken: cancellationToken);
            await TcpTesterUtil.WriteRandomData(stream, long.MaxValue, speedometer: null, cancellationToken: cancellationToken);

        }
        finally {
            Console.WriteLine($"Finish processing client. ClientEp: {remoteEndPoint}");
            client.Dispose();
        }
    }

    public void Dispose()
    {
        _tcpListener?.Stop();
    }
}
