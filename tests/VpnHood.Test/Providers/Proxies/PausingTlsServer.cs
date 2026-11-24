using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Server.Access;

namespace VpnHood.Test.Providers.Proxies;

// A VPN server that answers each TLS hello after a pause, the time a handshake takes it, and closes a connection that
// sends none within the limit from its accept, as the real one does after 10 seconds. It counts the connections it
// takes and notes when each hello came; with HoldsFirst, the first hello waits for the test's release besides
internal sealed class PausingTlsServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly X509Certificate2 _certificate = CertificateUtil.CreateSelfSigned();
    private readonly TaskCompletionSource _firstRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<Task> _answers = new();
    private Task _accepting = Task.CompletedTask;
    private int _acceptedCount;
    private int _helloCount;

    public TimeSpan Pause { get; init; }
    public TimeSpan HelloLimit { get; init; } = Timeout.InfiniteTimeSpan;
    public bool HoldsFirst { get; init; }
    public ConcurrentQueue<long> HelloTimes { get; } = new();
    public IPEndPoint EndPoint => (IPEndPoint)_listener.LocalEndpoint;
    public int AcceptedCount => Volatile.Read(ref _acceptedCount);

    public PausingTlsServer Start()
    {
        _listener.Start();
        _accepting = Accept(_cts.Token);
        return this;
    }

    public void ReleaseFirst()
    {
        _firstRelease.TrySetResult();
    }

    private async Task Accept(CancellationToken cancellationToken)
    {
        try {
            while (true) {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                Interlocked.Increment(ref _acceptedCount);
                _answers.Enqueue(Answer(client, cancellationToken));
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested) {
            // the server's close
        }
    }

    // its own client, closed at its end
    private async Task Answer(TcpClient client, CancellationToken cancellationToken)
    {
        try {
            // the hello, left for the handshake to read
            using var helloCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            helloCts.CancelAfter(HelloLimit);
            if (await client.Client.ReceiveAsync(new byte[1], SocketFlags.Peek, helloCts.Token) == 0)
                return;

            HelloTimes.Enqueue(Environment.TickCount64);
            if (HoldsFirst && Interlocked.Increment(ref _helloCount) == 1)
                await _firstRelease.Task.WaitAsync(cancellationToken);

            await Task.Delay(Pause, cancellationToken);
            await using var sslStream = new SslStream(client.GetStream());
            await sslStream.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions { ServerCertificate = _certificate }, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or SocketException or AuthenticationException
                                       or OperationCanceledException) {
            // no hello within the limit, or a client that went before its handshake's end
        }
        finally {
            client.Dispose();
        }
    }

    // after its accept loop's end and every answer's, which its token ends, so no handshake has the certificate
    public void Dispose()
    {
        _cts.Cancel();
        _accepting.GetAwaiter().GetResult();
        Task.WaitAll(_answers.ToArray());
        _listener.Stop();
        _certificate.Dispose();
        _cts.Dispose();
    }
}
