using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;

namespace VpnHood.Common.Utils;

// Use for debugging
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class AsyncStreamTracker(Stream sourceStream, bool leaveOpen)
    : AsyncStreamDecorator(sourceStream, leaveOpen)
{
    public string LogPrefix { get; set; } = "";

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogTrace(LogPrefix + "Reading count. " + count);
        return base.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogTrace(LogPrefix + "writing count. " + count);
        return base.WriteAsync(buffer, offset, count, cancellationToken);
    }
}