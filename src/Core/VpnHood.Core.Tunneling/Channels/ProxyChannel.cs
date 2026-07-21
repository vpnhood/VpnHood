using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Memory;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.Connections;

namespace VpnHood.Core.Tunneling.Channels;

public class ProxyChannel : IProxyChannel
{
    private bool _disposed;
    private readonly IStreamConnection _hostStreamConnection;
    private readonly IStreamConnection _tunnelStreamConnection;
    private readonly TransferBufferSize _tunnelBufferSize;
    private const int BufferSizeMax = 0x14000;
    private const int BufferSizeMin = 2048;
    private bool _started;
    private Traffic _traffic = new();
    private readonly Lock _trafficLock = new();
    private bool _isTunnelReadTaskFinished;
    private readonly Job _checkAliveJob;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private bool IsDisposed => _disposed;

    public DateTime LastActivityTime { get; private set; } = FastDateTime.Now;
    public string ChannelId { get; }
    private readonly TrafficMeter? _trafficMeter;

    public ProxyChannel(string channelId, IStreamConnection orgStreamConnection, IStreamConnection tunnelStreamConnection,
        TransferBufferSize tunnelBufferSize, TrafficMeter? trafficMeter = null)
    {
        _hostStreamConnection = orgStreamConnection;
        _tunnelStreamConnection = tunnelStreamConnection;
        _tunnelBufferSize = tunnelBufferSize;
        _trafficMeter = trafficMeter;
        VhTypeTracker.Track(this);

        if (_tunnelBufferSize.Receive is < BufferSizeMin or > BufferSizeMax)
            throw new ArgumentOutOfRangeException(
                $"Proxy receive buffer size must be greater than or equal to {BufferSizeMin} and less than {BufferSizeMax}. It was {_tunnelBufferSize.Receive}");

        if (_tunnelBufferSize.Send is < BufferSizeMin or > BufferSizeMax)
            throw new ArgumentOutOfRangeException(
                $"Proxy send buffer size must be greater than or equal to {BufferSizeMin} and less than {BufferSizeMax}. It was {_tunnelBufferSize.Send}");

        ChannelId = channelId;
        _checkAliveJob = new Job(CheckAlive, TunnelDefaults.TcpCheckInterval, nameof(ProxyChannel));
    }

    public Traffic Traffic {
        get {
            lock (_trafficLock)
                return _traffic;
        }
    }

    public PacketChannelState State {
        get {
            if (IsDisposed)
                return PacketChannelState.Disposed;

            return _started
                ? PacketChannelState.Connected
                : PacketChannelState.NotStarted;
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (_started)
            throw new InvalidOperationException("ProxyChannel is already started.");

        var startTask = StartInternal(_cancellationTokenSource.Token);
        VhTypeTracker.Track(startTask, "ProxyChannel.StartTask");
    }

    private async Task StartInternal(CancellationToken cancellationToken)
    {
        VhTypeTracker.Record("ProxyChannel.StartInternal.started");
        try {
            _started = true;

            var tunnelReadTask = CopyFromTunnelAsync(
                _tunnelStreamConnection.Stream, _hostStreamConnection.Stream, _tunnelBufferSize.Receive,
                cancellationToken, cancellationToken); // tunnel => host
            VhTypeTracker.Track(tunnelReadTask, "ProxyChannel.ReadPumpTask");

            var tunnelWriteTask = CopyToTunnelAsync(
                _hostStreamConnection.Stream, _tunnelStreamConnection.Stream, _tunnelBufferSize.Send,
                cancellationToken, cancellationToken); // host => tunnel
            VhTypeTracker.Track(tunnelWriteTask, "ProxyChannel.WritePumpTask");

            var completedTask = await Task.WhenAny(tunnelReadTask, tunnelWriteTask).Vhc();
            _isTunnelReadTaskFinished = completedTask == tunnelReadTask;

            // just to ensure that both tasks are completed gracefully, Connection should also handle it
            await Task.WhenAll(
                    _hostStreamConnection.Stream.DisposeAsync().AsTask(),
                    _tunnelStreamConnection.Stream.DisposeAsync().AsTask())
                .Vhc();

            // Observe the other task's completion and catch any exception to avoid UnobservedTaskException
            try {
                await Task.WhenAll(tunnelReadTask, tunnelWriteTask).Vhc();
            }
            catch {
                // Ignore. The exceptions are already logged inside CopyFromTunnelAsync / CopyToTunnelAsync
            }
        }
        catch (Exception ex) when (IsDisposed && VhLogger.IsSocketCloseException(ex)) {
            // this is normal shutdown for host stream, no need to log it
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel, ex,
                "Error while using a ProxyChannel. ChannelId: {ChannelId}, IsDisposed: {IsDisposed}",
                ChannelId, IsDisposed);
        }
        finally {
            VhTypeTracker.Record("ProxyChannel.StartInternal.completed");
            Dispose();
        }
    }

    private async Task CopyFromTunnelAsync(Stream source, Stream destination, int bufferSize,
        CancellationToken sourceCancellationToken, CancellationToken destinationCancellationToken)
    {
        VhTypeTracker.Record("ProxyChannel.ReadPump.started");
        try {
            var copyTask = CopyToInternalAsync(source, destination, false, bufferSize,
                sourceCancellationToken, destinationCancellationToken);
            VhTypeTracker.Track(copyTask, "ProxyChannel.ReadCopyTask");
            await copyTask.Vhc();
        }
        catch (Exception ex) when (IsDisposed && VhLogger.IsSocketCloseException(ex)) {
            // this is normal shutdown for host stream, no need to log it
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex,
                "ProxyChannel: Error while copying from tunnel. ChannelId: {ChannelId}, IsDisposed: {IsDisposed}"
                , ChannelId, IsDisposed);
            throw;
        }
        finally {
            VhTypeTracker.Record("ProxyChannel.ReadPump.completed");
        }
    }

    private async Task CopyToTunnelAsync(Stream source, Stream destination, int bufferSize,
        CancellationToken sourceCancellationToken, CancellationToken destinationCancellationToken)
    {
        VhTypeTracker.Record("ProxyChannel.WritePump.started");
        try {
            var copyTask = CopyToInternalAsync(source, destination, true, bufferSize,
                sourceCancellationToken, destinationCancellationToken);
            VhTypeTracker.Track(copyTask, "ProxyChannel.WriteCopyTask");
            await copyTask.Vhc();
        }
        catch (Exception ex) when (IsDisposed && VhLogger.IsSocketCloseException(ex)) {
            // this is normal shutdown for host stream, no need to log it
        }
        catch (Exception ex) {
            // tunnel read task has been finished, it is normal shutdown for host stream
            // because we dispose and cancel reading from host stream
            if (_isTunnelReadTaskFinished && VhLogger.IsSocketCloseException(ex))
                return;

            VhLogger.Instance.LogDebug(ex,
                "ProxyChannel: Error while copying to tunnel. ChannelId: {ChannelId}", ChannelId);
            throw;
        }
        finally {
            VhTypeTracker.Record("ProxyChannel.WritePump.completed");
        }
    }

    private async Task CopyToInternalAsync(Stream source, Stream destination, bool isSendingToTunnel, int bufferSize,
        CancellationToken sourceCt, CancellationToken destinationCt)
    {
        // Microsoft Stream Source Code:
        // We pick a value that is the largest multiple of 4096 that is still smaller than the large object heap threshold (85K).
        // The CopyTo/CopyToAsync buffer is short-lived and is likely to be collected at Gen0, and it offers a significant
        // improvement in Copy performance.
        // 0x14000 recommended by microsoft for copying buffers
        if (bufferSize > BufferSizeMax)
            throw new ArgumentException($"Buffer is too big, maximum supported size is {BufferSizeMax}",
                nameof(bufferSize));

        // use PreserveWriteBuffer if possible
        var destinationPreserved = destination as IPreservedChunkStream;
        var preserveCount = destinationPreserved?.PreserveWriteBufferLength ?? 0;

        // Pooled buffer (was `new byte[bufferSize]` — the most allocation-heavy spot in the app under
        // many concurrent channels). Rented once per pump direction, so the IMemoryOwner wrapper cost
        // is negligible; Rent may return a larger buffer, so slice to the requested size.
        using var readBufferOwner = System.Buffers.MemoryPool<byte>.Shared.Rent(bufferSize);
        var readBuffer = readBufferOwner.Memory[..bufferSize];
        VhTypeTracker.Track(readBufferOwner);
        if (MemoryMarshal.TryGetArray<byte>(readBufferOwner.Memory, out var segment) && segment.Array != null)
            VhTypeTracker.Track(segment.Array);
        while (!sourceCt.IsCancellationRequested && !destinationCt.IsCancellationRequested) {
            // read from source
            var bytesRead = await source
                .ReadAsync(readBuffer[preserveCount..], sourceCt)
                .Vhc();

            // check end of the stream
            if (bytesRead == 0)
                break;

            // write to destination
            if (destinationPreserved != null)
                await destinationPreserved.WritePreservedAsync(readBuffer[..(preserveCount + bytesRead)],
                    cancellationToken: destinationCt).Vhc();
            else
                await destination.WriteAsync(readBuffer[preserveCount..bytesRead], destinationCt).Vhc();

            // calculate transferred bytes
            lock (_trafficLock) {
                // update traffic usage
                if (isSendingToTunnel)
                    _traffic += new Traffic(bytesRead, 0);
                else
                    _traffic += new Traffic(0, bytesRead);

                // set LastActivityTime as some data delegated
                LastActivityTime = FastDateTime.Now;
            }

            // notify traffic meter and throttle if needed
            if (_trafficMeter != null) {
                if (isSendingToTunnel) {
                    _trafficMeter.OnSent(bytesRead);
                    await _trafficMeter.ThrottleSendAsync(sourceCt).Vhc();
                }
                else {
                    _trafficMeter.OnReceived(bytesRead);
                    await _trafficMeter.ThrottleReceiveAsync(sourceCt).Vhc();
                }
            }
        }
    }

    private ValueTask CheckAlive(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (!_started)
            return default;

        // check tcp states
        if (_hostStreamConnection.Connected && _tunnelStreamConnection.Connected)
            return default;

        VhLogger.Instance.LogInformation(GeneralEventId.ProxyChannel,
            "Disposing a ProxyChannel due to its error state. ChannelId: {ChannelId}", ChannelId);

        Dispose();
        return default;
    }

    public override string ToString()
    {
        return ChannelId;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        _checkAliveJob.Dispose();
        _started = false;
        _hostStreamConnection.Dispose();
        _tunnelStreamConnection.Dispose();
        VhTypeTracker.Record("ProxyChannel.disposed");
    }
}
