using Microsoft.Extensions.Logging;
using VpnHood.Core.TcpStack.Abstractions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.TcpStack;

/// <summary>
/// Encapsulates live diagnostic metrics and performance counters for a <see cref="LocalTcpStack"/> instance.
/// All metrics are updated thread-safely.
/// </summary>
/// <remarks>
/// This type also owns the lifecycle logging for established connections: callers report the establish/release
/// events (via <see cref="IncrementEstablishedConnections"/> / <see cref="DecrementEstablishedConnections"/>)
/// and the counter + log line are produced together, so the running live count can never drift from what is
/// logged. Lines are at Debug level — grep catlog for "[TcpStack]" (or "+CONN" / "-CONN").
/// </remarks>
public sealed class TcpStackDiagnostics
{
    private int _connectionCount;
    private int _peakConnectionCount;
    private int _establishedConnections;
    private long _totalPipeBufferedBytes;

    /// <summary>Gets the number of concurrent connections currently tracked.</summary>
    public int ConnectionCount => Volatile.Read(ref _connectionCount);

    /// <summary>Gets the high-water mark of concurrent connections tracked during the stack's lifetime.</summary>
    public int PeakConnectionCount => Volatile.Read(ref _peakConnectionCount);

    /// <summary>Gets the number of tracked connections that have successfully established a handshake.</summary>
    public int EstablishedConnections => Volatile.Read(ref _establishedConnections);

    /// <summary>Gets the aggregate number of bytes buffered across all connection reassembly pipes.</summary>
    public long TotalPipeBufferedBytes => Volatile.Read(ref _totalPipeBufferedBytes);

    /// <summary>Gets the window size of receive configured for this stack profile.</summary>
    public int ConfiguredReceiveWindow { get; internal set; }

    /// <summary>Gets the maximum number of simultaneous connections configured for this stack.</summary>
    public int ConfiguredMaxConnections { get; internal set; }

    internal void SetConnectionCount(int count)
    {
        Volatile.Write(ref _connectionCount, count);

        int peak;
        do {
            peak = Volatile.Read(ref _peakConnectionCount);
            if (count <= peak) break;
        } while (Interlocked.CompareExchange(ref _peakConnectionCount, count, peak) != peak);
    }

    /// <summary>
    /// Records that a connection completed its handshake: bumps the live established count and logs the
    /// event so TCP-stack stream creation can be monitored via catlog.
    /// </summary>
    internal void IncrementEstablishedConnections(IPEndPointPairValue endPointPair)
    {
        var live = Interlocked.Increment(ref _establishedConnections);
        VhLogger.Instance.LogDebug(TcpStackEventIds.TcpStackDiag,
            "[TcpStack] +CONN established {EndPointPair} live={LiveEstablished}", endPointPair, live);
    }

    /// <summary>
    /// Records that an established connection was released: drops the live established count and logs the
    /// event (with the teardown <paramref name="reason"/>) so TCP-stack stream teardown can be monitored via catlog.
    /// </summary>
    internal void DecrementEstablishedConnections(IPEndPointPairValue endPointPair, string reason)
    {
        var live = Interlocked.Decrement(ref _establishedConnections);
        VhLogger.Instance.LogDebug(TcpStackEventIds.TcpStackDiag,
            "[TcpStack] -CONN released({Reason}) {EndPointPair} live={LiveEstablished}", reason, endPointPair, live);
    }

    internal void AddPipeBufferedBytes(long bytes) => Interlocked.Add(ref _totalPipeBufferedBytes, bytes);
}
