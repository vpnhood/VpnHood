using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Net.Toolkit.Monitoring;
using VpnHood.Net.Toolkit.Sockets;

namespace VpnHood.Test.Providers.Proxies;

// a connector with no proxy: it connects straight to the server and hands out the socket's stream wrapped, so a
// test sees what goes through the stream it hands out
internal sealed class StreamCountingProxyConnector : IProxyConnector
{
    private int _writtenByteCount;
    public int WrittenByteCount => Volatile.Read(ref _writtenByteCount);
    public bool IsEnabled => true;
    public bool UseRecentSucceeded { get; set; }
    public ProgressStatus? Progress => null;

    public ProxyConnectorStatus Status => new() {
        AutoUpdate = false, SessionStatus = new ProxySessionStatus(), IsAnySucceeded = true,
        SucceededServerCount = 1, FailedServerCount = 0, UnknownServerCount = 0, DisabledServerCount = 0
    };

    public async Task<ProxyConnection> ConnectAsync(ISocketFactory socketFactory, IPEndPoint ipEndPoint,
        string? hostName, RemoteCertificateValidationCallback? serverCertificateValidation, Action? onAttempt,
        CancellationToken cancellationToken)
    {
        var tcpClient = socketFactory.CreateTcpClient(ipEndPoint);
        await tcpClient.ConnectAsync(ipEndPoint, cancellationToken);
        return new ProxyConnection(tcpClient, new CountingStream(tcpClient.GetStream(),
            count => Interlocked.Add(ref _writtenByteCount, count)));
    }

    public void RecordFailed(TcpClient tcpClient, Exception ex) { }

    public Task<bool> WaitForEndPoints(CancellationToken cancellationToken) => Task.FromResult(true);

    public Task CheckServers(ISocketFactory socketFactory, IPEndPoint ipEndPoint, string? hostName,
        RemoteCertificateValidationCallback? serverCertificateValidation, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task UpdateOptions(ProxyOptions proxyOptions, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task Flush(CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() => default;
}
