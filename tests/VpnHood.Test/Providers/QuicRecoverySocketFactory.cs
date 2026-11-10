using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Toolkit.Sockets;

namespace VpnHood.Test.Providers;

internal sealed class QuicRecoverySocketFactory : TestSocketFactory
{
    private int _tcpCount;
    private int _quicStreamCount;
    public int TcpCount => Volatile.Read(ref _tcpCount);
    public int QuicStreamCount => Volatile.Read(ref _quicStreamCount);
    public bool RejectTcpConnections { get; set; }
    public bool RejectQuicConnects { get; set; }
    public bool RejectQuicStreamOpens { get; set; }

    public override TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null)
    {
        Interlocked.Increment(ref _tcpCount);
        if (RejectTcpConnections)
            throw new SocketException((int)SocketError.ConnectionReset);
        return base.CreateTcpClient(ipEndPoint, options);
    }

    public void RecordQuicStreamOpen() => Interlocked.Increment(ref _quicStreamCount);

    public override IQuicClient CreateClient() => new QuicRecoveryClient(base.CreateClient(), this);
}
