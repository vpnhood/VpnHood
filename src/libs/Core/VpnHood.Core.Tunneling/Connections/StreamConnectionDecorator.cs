using System.Net;
using VpnHood.Net.Toolkit.Extensions;
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

    /// <inheritdoc/>
    /// <remarks>
    /// It closes its own stream, if it has one, then the wrapped connection, at once; a failure of its stream's close
    /// leaves the wrapped connection as it is.
    /// </remarks>
    public virtual void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        stream?.Dispose();
        InnerStreamConnection.Dispose();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It closes its own stream, if it has one, then releases the wrapped connection, which a failure of its stream's
    /// close skips; a second call waits for the first one's end.
    /// </remarks>
    public virtual async ValueTask DisposeAsync()
    {
        // a second close waits for the first one's end. No early return after it: the inner connection's async
        // close, which a quick close may have started without waiting, ends before this one does
        using var scopeLock = await _disposeLock.LockAsync(CancellationToken.None).Vhc();
        if (stream != null)
            await stream.DisposeAsync().Vhc();

        await InnerStreamConnection.DisposeAsync().Vhc();
        Dispose();
    }
}