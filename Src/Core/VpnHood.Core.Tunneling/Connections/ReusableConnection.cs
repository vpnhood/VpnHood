using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Channels.Streams;

namespace VpnHood.Core.Tunneling.Connections;

public delegate void ReuseConnectionCallback(ReusableConnection reusableConnection);
public class ReusableConnection : ConnectionDecorator
{
    private readonly Lock _reuseLock = new();
    private readonly ReuseConnectionCallback _reuseConnectionCallback;
    private bool _allowReuse = true;
    private bool _reusing;

    public ReusableConnection(IConnection connection, ReuseConnectionCallback reuseConnectionCallback)
        : base(connection)
    {
        _reuseConnectionCallback = reuseConnectionCallback;

        VhLogger.Instance.LogDebug(GeneralEventId.Stream,
            "A ReusableConnection has been created. ConnectionId: {ConnectionId}, StreamType: {StreamType}, LocalEp: {LocalEp}, RemoteEp: {RemoteEp}",
            ConnectionId, connection.Stream.GetType().Name, VhLogger.Format(LocalEndPoint),
            VhLogger.Format(RemoteEndPoint));
    }

    private bool CanReuse => _allowReuse && Stream is ChunkStream { CanReuse: true };

    public void PreventReuse()
    {
        lock (_reuseLock) {
            _allowReuse = false;
            if (Stream is ChunkStream chunkStream)
                chunkStream.PreventReuse();
        }
    }

    private async Task ReuseThenDispose()
    {
        try {
            _reusing = true;
            VhLogger.Instance.LogDebug(GeneralEventId.Stream,
                "Reusing a connection. ConnectionId: {ConnectionId}", ConnectionId);

            // verify if we can reuse the stream
            if (Stream is not ChunkStream chunkStream)
                throw new InvalidOperationException("Can not reuse the stream when stream is not ChunkStream.");

            await Reuse(chunkStream, _reuseConnectionCallback).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(GeneralEventId.Stream, ex,
                "Could not reuse the connection. ConnectionId: {ConnectionId}", ConnectionId);

            // dispose and we should not try to reuse the stream
            PreventReuse();
            await DisposeAsync();
        }
        finally {
            _reusing = false;
        }
    }

    private async Task Reuse(ChunkStream chunkStream, ReuseConnectionCallback reuseConnectionCallback)
    {
        var newStream = await chunkStream.CreateReuse().Vhc();
        lock (_reuseLock) {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // we don't call SuppressFlow on debug to allow MsTest shows console output in nested task
            using IDisposable? suppressFlow = IsDebug ? null : ExecutionContext.SuppressFlow();
            var connectionDecorator = new ConnectionDecorator(_innerConnection, newStream);
            var reusableConnection = new ReusableConnection(connectionDecorator, reuseConnectionCallback);
            Task.Run(() => reuseConnectionCallback(reusableConnection))
                .ContinueWith(task => VhLogger.Instance.LogError(GeneralEventId.Stream, task.Exception,
                        "Reuse callback failed. ConnectionId: {ConnectionId}", ConnectionId),
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            
            _disposed = true;
        }
    }

    public override void Dispose()
    {
        // prevent dispose when reuse is almost done. the owner is going to change soon 
        lock (_reuseLock) {
            if (_disposed)
                return;

            // If reuse is allowed, and we're not currently reusing, start reuse.
            if (_allowReuse) {
                // check if we are in the process of reusing the stream
                if (_reusing)
                    return;

                // reuse the stream in background
                // it will set _disposed to true 
                if (CanReuse) {
                    _ = ReuseThenDispose();
                    return;
                }
            }

            // prevent future reuse and stream reuse
            PreventReuse();

            // close stream and let cancel reuse if it is in progress
            base.Dispose();
            _disposed = true;
        }
    }

    public override ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

#if DEBUG
    private static bool IsDebug => true;
#else
    private static bool IsDebug => false;
#endif

}