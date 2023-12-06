using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;

namespace VpnHood.Common.Utils;

public class AsyncStreamTracker : AsyncStreamDecorator
{
    public string LogPrefix { get; set; }= "";

    public AsyncStreamTracker(Stream sourceStream, bool leaveOpen) 
        : base(sourceStream, leaveOpen)
    {
    }

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