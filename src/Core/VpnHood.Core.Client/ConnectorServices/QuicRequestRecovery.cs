using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.ConnectorServices;

// An invalid proof on h3 must receive HTTP/3 behavior, so it cannot carry the HTTP/1 Date that corrects
// our clock on TCP. After failed admission on an established QUIC stream, the next request asks the TLS peer
// for that Date. The failed application request is never replayed, and healthy QUIC requests cost no probe.
internal sealed class QuicRequestRecovery(
    TcpStreamConnectionFactory tcpConnectionFactory, ClientRequestSigner requestSigner, VpnEndPoint vpnEndPoint,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FailedProbeRetryDelay = TimeSpan.FromMinutes(1);
    private long? _failedAt;
    private long _pending;
    private int _probing;

    public void RequireRecovery() => Interlocked.Increment(ref _pending);

    // Only this caller waits for its probe. Other requests keep using QUIC while it runs.
    public async Task Recover(string connectionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _pending) == 0 || Interlocked.CompareExchange(ref _probing, 1, 0) != 0)
            return;

        try {
            var pending = Volatile.Read(ref _pending);
            if (pending == 0)
                return;
            // TCP can be blocked while QUIC works; a failed probe must not delay every request.
            if (_failedAt is { } failedAt && timeProvider.GetElapsedTime(failedAt) < FailedProbeRetryDelay)
                return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);
            await Probe(connectionId, timeout.Token).Vhc();
            cancellationToken.ThrowIfCancellationRequested();
            _failedAt = null;
            // A request that failed during this probe still needs its own recovery opportunity.
            Interlocked.CompareExchange(ref _pending, 0, pending);
        }
        catch (OperationCanceledException) {
            cancellationToken.ThrowIfCancellationRequested();
            _failedAt = timeProvider.GetTimestamp();
            // This probe's deadline expired; QUIC still gets its own request attempt.
        }
        catch (Exception ex) {
            cancellationToken.ThrowIfCancellationRequested();
            _failedAt = timeProvider.GetTimestamp();
            VhLogger.Instance.LogDebug(ex, "Could not obtain a TLS clock correction for QUIC.");
        }
        finally {
            Volatile.Write(ref _probing, 0);
        }
    }

    private async Task Probe(string connectionId, CancellationToken cancellationToken)
    {
        var correctionVersion = requestSigner.ClockCorrectionVersion;
        var connection = await tcpConnectionFactory.CreateConnection(
            connectionId + ":clock", isTcpPacketChannel: false, onConnectAttempt: null, cancellationToken).Vhc();
        // The probe's deadline also closes the socket during a pending read or graceful TLS close.
        using var closeOnCancellation = cancellationToken.Register(connection.Dispose);
        try {
            var pathBase = vpnEndPoint.PathBase?.Trim('/');
            var path = string.IsNullOrEmpty(pathBase) ? "/" : $"/{pathBase}/";
            var head = $"GET {path}{Guid.NewGuid()} HTTP/1.1\r\nHost: {vpnEndPoint.HostName}\r\nConnection: close\r\n\r\n";
            await connection.Stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellationToken).Vhc();
            using var response = await HttpUtils.ReadResponse(connection.Stream, cancellationToken).Vhc();
            if (response.StatusCode != HttpStatusCode.Unauthorized || response.Headers.Date == null)
                throw new IOException("The TLS clock probe did not return an unauthorized response with a Date header.");

            // A meaningful correction is optional: a transport failure can happen with an accurate clock too.
            // The signer ignores an older response if another request corrected its clock in the meantime.
            requestSigner.TryCorrectClock(response.Headers.Date, correctionVersion);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch {
            // The cancellation callback can close a socket just before an awaited read observes its token.
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        finally {
            // Keep the cancellation registration alive until the graceful TLS close has finished.
            await connection.DisposeAsync().Vhc();
        }
    }
}
