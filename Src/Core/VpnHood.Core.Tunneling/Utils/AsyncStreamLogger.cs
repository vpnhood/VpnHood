using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Streams;

namespace VpnHood.Core.Tunneling.Utils;

// ReSharper disable once UnusedType.Global
public class StreamDecoratorAsyncStreamTracker(Stream sourceStream, bool leaveOpen)
    : StreamDecoratorAsync(sourceStream, leaveOpen)
{
    public string LogPrefix { get; set; } = "";

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (VhLogger.MinLogLevel == LogLevel.Trace)
            VhLogger.Instance.LogTrace(LogPrefix + "Reading count. " + buffer.Length);
        return base.ReadAsync(buffer, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (VhLogger.MinLogLevel == LogLevel.Trace)
            VhLogger.Instance.LogTrace(LogPrefix + "writing count. " + buffer.Length);
        return base.WriteAsync(buffer, cancellationToken);
    }
}