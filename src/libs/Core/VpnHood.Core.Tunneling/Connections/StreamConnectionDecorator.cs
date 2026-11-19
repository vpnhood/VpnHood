using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Tunneling.Connections;

/// <summary>
/// Represents a connection over another, which passes everything on to it, and may read and write through a stream of
/// its own over the other's, as a buffer.
/// </summary>
/// <param name="streamConnection">The connection it wraps.</param>
/// <param name="stream">
/// The stream to read and write through, which it owns, or <see langword="null"/> for the wrapped connection's.
/// </param>
public class StreamConnectionDecorator(IStreamConnection streamConnection, Stream? stream = null)
    : IStreamConnection
{
    private readonly AsyncLock _disposeLock = new();
    private bool _disposed;
    private bool _isStreamClosed; // its own stream closes once, by whichever close comes first
    private IStreamConnection InnerStreamConnection => streamConnection;

    /// <inheritdoc/>
    public virtual bool Connected => !_disposed && InnerStreamConnection.Connected;

    /// <inheritdoc/>
    /// <remarks>It is the given stream where there is one, otherwise the wrapped connection's.</remarks>
    public Stream Stream => stream ?? InnerStreamConnection.Stream;

    /// <inheritdoc/>
    public IPEndPoint LocalEndPoint => InnerStreamConnection.LocalEndPoint;

    /// <inheritdoc/>
    public IPEndPoint RemoteEndPoint => InnerStreamConnection.RemoteEndPoint;

    /// <inheritdoc/>
    public bool IsServer => InnerStreamConnection.IsServer;

    /// <inheritdoc/>
    public string ConnectionName  => InnerStreamConnection.ConnectionName;

    /// <inheritdoc/>
    public string ConnectionId {
        get => InnerStreamConnection.ConnectionId;
        set => InnerStreamConnection.ConnectionId = value;
    }

    /// <inheritdoc/>
    public bool RequireHttpResponse {
        get => InnerStreamConnection.RequireHttpResponse;
        set => InnerStreamConnection.RequireHttpResponse = value;
    }

    /// <inheritdoc/>
    public virtual void PreventReuse() => InnerStreamConnection.PreventReuse();

    // A close of its own stream, as a WebSocket's closing frames: one that fails leaves the wrapped connection in no
    // state to carry another request
    private bool TryCloseStream(Action close)
    {
        try {
            close();
            return true;
        }
        catch (Exception ex) {
            LogStreamCloseFailure(ex);
            return false;
        }
    }

    private void LogStreamCloseFailure(Exception ex)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Stream, ex,
            "Could not close a connection's stream; the connection closes with no reuse. ConnectionId: {ConnectionId}",
            ConnectionId);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It closes its own stream, if it has one, then the wrapped connection, at once. A failed close of its stream is
    /// logged, and the wrapped connection closes all the same, with no reuse.
    /// </remarks>
    public virtual void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        if (stream != null && !Interlocked.Exchange(ref _isStreamClosed, true) && !TryCloseStream(stream.Dispose))
            InnerStreamConnection.PreventReuse();

        InnerStreamConnection.Dispose();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It closes its own stream, if it has one, then releases the wrapped connection. A failed close of its stream is
    /// logged, and the wrapped connection is released all the same, with no reuse. A second call waits for the first
    /// one's end.
    /// </remarks>
    public virtual async ValueTask DisposeAsync()
    {
        // a second close waits for the first one's end. No early return after it: the inner connection's async
        // close, which a quick close may have started without waiting, ends before this one does
        using var scopeLock = await _disposeLock.LockAsync(CancellationToken.None).Vhc();

        // the stream's close is judged before the wrapped connection's release, which may hand that connection on for
        // reuse; a quick close during it still closes the wrapped connection at once
        if (stream != null && !Interlocked.Exchange(ref _isStreamClosed, true)) {
            try {
                await stream.DisposeAsync().Vhc();
            }
            catch (Exception ex) {
                LogStreamCloseFailure(ex);
                InnerStreamConnection.PreventReuse();
            }
        }

        await InnerStreamConnection.DisposeAsync().Vhc();
        Volatile.Write(ref _disposed, true);
    }
}