using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Common.JobController;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Tunneling.ClientStreams;

namespace VpnHood.Tunneling.Channels;

public class StreamProxyChannel : IChannel, IJob
{
    private readonly int _orgStreamBufferSize;
    private readonly IClientStream _hostTcpClientStream;
    private readonly int _tunnelStreamBufferSize;
    private readonly IClientStream _tunnelTcpClientStream;
    private readonly CancellationTokenSource _hostCancellationTokenSource = new();
    private readonly CancellationTokenSource _tunnelCancellationTokenSource = new();
    private const int BufferSizeDefault = 0x1000 * 4; //16k
    private const int BufferSizeMax = 0x14000;
    private const int BufferSizeMin = 0x1000;
    private bool _disposed;
    private bool _closing;
    private Task _startTask = Task.CompletedTask;

    public JobSection JobSection { get; }
    public bool Connected { get; private set; }
    public Traffic Traffic { get; } = new();
    public DateTime LastActivityTime { get; private set; } = FastDateTime.Now;
    public string ChannelId { get; }

    public StreamProxyChannel(string channelId, IClientStream orgClientStream, IClientStream tunnelClientStream,
        TimeSpan tcpTimeout, int? orgStreamBufferSize = BufferSizeDefault, int? tunnelStreamBufferSize = BufferSizeDefault)
    {
        _hostTcpClientStream = orgClientStream ?? throw new ArgumentNullException(nameof(orgClientStream));
        _tunnelTcpClientStream = tunnelClientStream ?? throw new ArgumentNullException(nameof(tunnelClientStream));

        // validate buffer sizes
        if (orgStreamBufferSize is 0 or null) orgStreamBufferSize = BufferSizeDefault;
        if (tunnelStreamBufferSize is 0 or null) tunnelStreamBufferSize = BufferSizeDefault;

        _orgStreamBufferSize = orgStreamBufferSize is >= BufferSizeMin and <= BufferSizeMax
            ? orgStreamBufferSize.Value
            : throw new ArgumentOutOfRangeException(nameof(orgStreamBufferSize), orgStreamBufferSize,
                $"Value must be greater or equal than {BufferSizeMin} and less than {BufferSizeMax}.");

        _tunnelStreamBufferSize = tunnelStreamBufferSize is >= BufferSizeMin and <= BufferSizeMax
            ? tunnelStreamBufferSize.Value
            : throw new ArgumentOutOfRangeException(nameof(tunnelStreamBufferSize), tunnelStreamBufferSize,
                $"Value must be greater or equal than {BufferSizeMin} and less than {BufferSizeMax}");

        ChannelId = channelId;
        JobSection = new JobSection(tcpTimeout);
        JobRunner.Default.Add(this);
    }

    public void Start()
    {
        _startTask = StartInternal();
    }

    private async Task StartInternal()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        if (Connected)
            throw new InvalidOperationException("StreamProxyChannel is already started.");

        Connected = true;
        var tunnelCopyTask = Task.CompletedTask;
        var hostCopyTask = Task.CompletedTask;
        try
        {
            // let pass CancellationToken for the host only to save the tunnel for reuse

            tunnelCopyTask = CopyToAsync(
                _tunnelTcpClientStream.Stream, _hostTcpClientStream.Stream, false, _tunnelStreamBufferSize,
                _tunnelCancellationTokenSource.Token, _hostCancellationTokenSource.Token); // tunnel => host

            hostCopyTask = CopyToAsync(
                _hostTcpClientStream.Stream, _tunnelTcpClientStream.Stream, true, _orgStreamBufferSize,
                _hostCancellationTokenSource.Token, _tunnelCancellationTokenSource.Token); // host = tunnel

            await Task.WhenAny(tunnelCopyTask, hostCopyTask);
        }
        finally
        {
            _closing = true;

            // let tasks shutdown gracefully
            var gracefulDelayTask = Task.Delay(TunnelDefaults.TcpGracefulTimeout);
            var task1 = Task.WhenAny(tunnelCopyTask, gracefulDelayTask);
            var task2 = Task.WhenAny(hostCopyTask, gracefulDelayTask);
            await Task.WhenAll(task1, task2);


            // close tunnel if it is not closed yet
            _hostCancellationTokenSource.Cancel();
            _tunnelCancellationTokenSource.Cancel();
            await Task.WhenAll(hostCopyTask, tunnelCopyTask);
            await Task.WhenAll(_hostTcpClientStream.DisposeAsync().AsTask(), _tunnelTcpClientStream.DisposeAsync().AsTask());

            Connected = false;
        }
    }

    public Task RunJob()
    {
        // if either task is completed let graceful shutdown do its job
        if (_disposed || !Connected || _closing)
            return Task.CompletedTask;

        CheckClientIsAlive();
        return Task.CompletedTask;
    }

    private void CheckClientIsAlive()
    {
        // check tcp states
        if (_hostTcpClientStream.CheckIsAlive() && _tunnelTcpClientStream.CheckIsAlive())
            return;

        VhLogger.Instance.LogInformation(GeneralEventId.StreamProxyChannel,
            "Disposing a StreamProxyChannel due to its error state. ChannelId: {ChannelId}", ChannelId);

        _hostCancellationTokenSource.Cancel();
    }

    private async Task CopyToAsync(Stream source, Stream destination, bool isDestinationTunnel, int bufferSize,
        CancellationToken sourceCancellationToken, CancellationToken destinationCancellationToken)
    {
        try
        {
            await CopyToInternalAsync(source, destination, isDestinationTunnel, bufferSize,
                sourceCancellationToken, destinationCancellationToken);
        }
        catch (Exception ex)
        {
            // Dispose if any side throw an exception
            VhLogger.LogError(GeneralEventId.Tcp, ex,
                "StreamProxyChannel: Error in copying {direction} tunnel. ChannelId: {ChannelId}",
                isDestinationTunnel ? "to" : "from", ChannelId);
        }
    }

    private async Task CopyToInternalAsync(Stream source, Stream destination, bool isSendingOut, int bufferSize,
        CancellationToken sourceCancellationToken, CancellationToken destinationCancellationToken)
    {
        var doubleBuffer = false; //i am not sure it could help!

        // Microsoft Stream Source Code:
        // We pick a value that is the largest multiple of 4096 that is still smaller than the large object heap threshold (85K).
        // The CopyTo/CopyToAsync buffer is short-lived and is likely to be collected at Gen0, and it offers a significant
        // improvement in Copy performance.
        // 0x14000 recommended by microsoft for copying buffers
        if (bufferSize > BufferSizeMax)
            throw new ArgumentException($"Buffer is too big, maximum supported size is {BufferSizeMax}",
                nameof(bufferSize));

        // <<----------------- the MOST memory consuming in the APP! >> ----------------------
        var readBuffer = new byte[bufferSize];
        var writeBuffer = doubleBuffer ? new byte[readBuffer.Length] : null;
        Task? writeTask = null;
        while (!sourceCancellationToken.IsCancellationRequested && !destinationCancellationToken.IsCancellationRequested)
        {
            // read from source
            var bytesRead = await source.ReadAsync(readBuffer, 0, readBuffer.Length, sourceCancellationToken);
            if (writeTask != null)
                await writeTask;

            // check end of the stream
            if (bytesRead == 0)
                break;

            // write to destination
            if (writeBuffer != null)
            {
                Array.Copy(readBuffer, writeBuffer, bytesRead);
                writeTask = destination.WriteAsync(writeBuffer, 0, bytesRead, destinationCancellationToken);
            }
            else
            {
                await destination.WriteAsync(readBuffer, 0, bytesRead, destinationCancellationToken);
            }

            // calculate transferred bytes
            if (!isSendingOut)
                Traffic.Received += bytesRead;
            else
                Traffic.Sent += bytesRead;

            // set LastActivityTime as some data delegated
            LastActivityTime = FastDateTime.Now;
        }
    }


    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _hostCancellationTokenSource.Cancel();
        await _startTask;
    }
}