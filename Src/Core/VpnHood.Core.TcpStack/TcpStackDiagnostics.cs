using System.Threading;

namespace VpnHood.Core.TcpStack;

/// <summary>
/// Encapsulates live diagnostic metrics and performance counters for a <see cref="LocalTcpStack"/> instance.
/// All metrics are updated thread-safely.
/// </summary>
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

    /// <summary>Gets the receive window size configured for this stack profile.</summary>
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

    internal void IncrementEstablishedConnections() => Interlocked.Increment(ref _establishedConnections);
    internal void DecrementEstablishedConnections() => Interlocked.Decrement(ref _establishedConnections);
    internal void AddPipeBufferedBytes(long bytes) => Interlocked.Add(ref _totalPipeBufferedBytes, bytes);
}
