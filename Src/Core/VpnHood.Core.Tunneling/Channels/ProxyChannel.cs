using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.ClientStreams;

namespace VpnHood.Core.Tunneling.Channels;

public class ProxyChannel : IProxyChannel
{
    private int _isDisposed;
    private readonly IClientStream _hostClientStream;
    private readonly IClientStream _tunnelClientStream;
    private readonly TransferBufferSize _tunnelBufferSize;
    private const int BufferSizeMax = 0x14000;
    private const int BufferSizeMin = 2048;
    private bool _started;
    private Traffic _traffic = new();
    private readonly object _trafficLock = new();
    private bool _isTunnelReadTaskFinished;
    private readonly Job _checkAliveJob;
    private bool IsDisposed => _isDisposed == 1;

    public DateTime LastActivityTime { get; private set; } = FastDateTime.Now;
    public string ChannelId { get; }

    public ProxyChannel(string channelId, IClientStream orgClientStream, IClientStream tunnelClientStream,
        TransferBufferSize tunnelBufferSize)
    {
        _hostClientStream = orgClientStream ?? throw new ArgumentNullException(nameof(orgClientStream));
        _tunnelClientStream = tunnelClientStream ?? throw new ArgumentNullException(nameof(tunnelClientStream));
        _tunnelBufferSize = tunnelBufferSize;

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

        _ = StartInternal();
    }

    private async Task StartInternal()
    {
        try {
            _started = true;

            var tunnelReadTask = CopyFromTunnelAsync(
                _tunnelClientStream.Stream, _hostClientStream.Stream, _tunnelBufferSize.Receive,
                CancellationToken.None, CancellationToken.None); // tunnel => host

            var tunnelWriteTask = CopyToTunnelAsync(
                _hostClientStream.Stream, _tunnelClientStream.Stream, _tunnelBufferSize.Send,
                CancellationToken.None, CancellationToken.None); // host => tunnel

            var completedTask = await Task.WhenAny(tunnelReadTask, tunnelWriteTask).Vhc();
            _isTunnelReadTaskFinished = completedTask == tunnelReadTask;

            // just to ensure that both tasks are completed gracefully, ClientStream should also handle it
            await Task.WhenAll(
                    _hostClientStream.Stream.DisposeAsync().AsTask(),
                    _tunnelClientStream.Stream.DisposeAsync().AsTask())
                .Vhc();
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
            Dispose();
        }
    }

    private async Task CopyFromTunnelAsync(Stream source, Stream destination, int bufferSize,
        CancellationToken sourceCancellationToken, CancellationToken destinationCancellationToken)
    {
        try {
            await CopyToInternalAsync(source, destination, false, bufferSize,
                sourceCancellationToken, destinationCancellationToken).Vhc();
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
    }

    private async Task CopyToTunnelAsync(Stream source, Stream destination, int bufferSize,
        CancellationToken sourceCancellationToken, CancellationToken destinationCancellationToken)
    {
        try {
            await CopyToInternalAsync(source, destination, true, bufferSize,
                sourceCancellationToken, destinationCancellationToken).Vhc();
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

        // <<----------------- the MOST memory consuming in the APP! >> ----------------------
        Memory<byte> readBuffer = new byte[bufferSize];
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
        }
    }


    private ValueTask CheckAlive(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (!_started)
            return default;

        // check tcp states
        if (_hostClientStream.Connected && _tunnelClientStream.Connected)
            return default;

        VhLogger.Instance.LogInformation(GeneralEventId.ProxyChannel,
            "Disposing a ProxyChannel due to its error state. ChannelId: {ChannelId}", ChannelId);

        Dispose();
        return default;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
            return;

        _checkAliveJob.Dispose();
        _started = false;
        _hostClientStream.Dispose();
        _tunnelClientStream.Dispose();
    }
}