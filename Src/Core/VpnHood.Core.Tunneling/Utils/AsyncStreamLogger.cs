using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling.Utils;

// Use for debugging
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class AsyncStreamTracker(Stream sourceStream, bool leaveOpen)
    : AsyncStreamDecorator(sourceStream, leaveOpen)
{
    public string LogPrefix { get; set; } = "";

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (VhLogger.IsDiagnoseMode)
            VhLogger.Instance.LogTrace(LogPrefix + "Reading count. " + buffer.Length);
        return base.ReadAsync(buffer, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (VhLogger.IsDiagnoseMode)
            VhLogger.Instance.LogTrace(LogPrefix + "writing count. " + count);
        return base.WriteAsync(buffer, offset, count, cancellationToken);
    }
}