using System.Net;
using System.Text;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.ConnectorServices;

// An invalid proof on h3 must receive HTTP/3 behavior, so it cannot carry the HTTP/1 Date that corrects
// our clock on TCP. After a failed fresh QUIC request, the next one first asks the verified TLS peer
// for that Date. The failed application request is never replayed, and healthy QUIC requests cost no probe.
internal sealed class QuicRequestRecovery(
    TcpStreamConnectionFactory tcpConnectionFactory, ClientRequestSigner requestSigner, VpnEndPoint vpnEndPoint)
{
    private readonly AsyncLock _recoveryLock = new();
    private int _pending;

    public void RequireRecovery() => Volatile.Write(ref _pending, 1);

    // Called inside the existing fresh-connect gate and with the request's original deadline. Concurrent
    // failures share one successful probe; failure or cancellation leaves recovery pending for the next call.
    public async Task Recover(string connectionId, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _pending) == 0)
            return;

        using var scope = await _recoveryLock.LockAsync(cancellationToken).Vhc();
        if (Volatile.Read(ref _pending) == 0)
            return;

        var correctionVersion = requestSigner.ClockCorrectionVersion;
        var connection = await tcpConnectionFactory.CreateConnection(
            connectionId + ":clock", isTcpPacketChannel: false, onConnectAttempt: null, cancellationToken).Vhc();
        // Closing the socket also bounds a pending read or graceful close when this request's deadline ends.
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
            Volatile.Write(ref _pending, 0);
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
