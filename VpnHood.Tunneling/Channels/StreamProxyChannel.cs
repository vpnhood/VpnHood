﻿using System;
using System.ComponentModel.Design;
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
    private const int BufferSizeDefault = 0x1000 * 4; //16k
    private const int BufferSizeMax = 0x14000;
    private const int BufferSizeMin = 0x1000;
    private bool _disposed;

    public JobSection JobSection { get; } = new (TunnelDefaults.TcpCheckInterval);
    public bool Connected { get; private set; }
    public Traffic Traffic { get; } = new();
    public DateTime LastActivityTime { get; private set; } = FastDateTime.Now;
    public string ChannelId { get; }

    public StreamProxyChannel(string channelId, IClientStream orgClientStream, IClientStream tunnelClientStream,
        int? orgStreamBufferSize = BufferSizeDefault, int? tunnelStreamBufferSize = BufferSizeDefault)
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
        JobRunner.Default.Add(this);
    }

    public void Start()
    {
        _ = StartInternal();
    }

    private async Task StartInternal()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        if (Connected)
            throw new InvalidOperationException("StreamProxyChannel is already started.");

        Connected = true;
        try
        {
            // let pass CancellationToken for the host only to save the tunnel for reuse

            var tunnelCopyTask = CopyToAsync(
                _tunnelTcpClientStream.Stream, _hostTcpClientStream.Stream, false, _tunnelStreamBufferSize,
                CancellationToken.None, CancellationToken.None); // tunnel => host

            var hostCopyTask = CopyToAsync(
                _hostTcpClientStream.Stream, _tunnelTcpClientStream.Stream, true, _orgStreamBufferSize,
                CancellationToken.None, CancellationToken.None); // host = tunnel

            await Task.WhenAny(tunnelCopyTask, hostCopyTask);
        }
        finally
        {
            _ = DisposeAsync();
        }
    }

    public Task RunJob()
    {
        // if either task is completed let graceful shutdown do its job
        if (_disposed || !Connected)
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

        _ = DisposeAsync();
    }

    private bool _isFinished;
    private async Task CopyToAsync(Stream source, Stream destination, bool isDestinationTunnel, int bufferSize,
        CancellationToken sourceCancellationToken, CancellationToken destinationCancellationToken)
    {
        try
        {
            await CopyToInternalAsync(source, destination, isDestinationTunnel, bufferSize,
                sourceCancellationToken, destinationCancellationToken);
            _isFinished = true;
        }
        catch (Exception ex)
        {
            // show error if the stream hasn't been finished
            if (_isFinished && VhLogger.IsSocketCloseException(ex))
                return;

            var log = isDestinationTunnel
                ? "StreamProxyChannel: Error in copying to tunnel. ChannelId: {ChannelId}"
                : "StreamProxyChannel: Error in copying from tunnel. ChannelId: {ChannelId}";
            VhLogger.LogError(GeneralEventId.Tcp, ex, log, ChannelId);
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

    private readonly AsyncLock _disposeLock = new();
    private ValueTask? _disposeTask;
    public ValueTask DisposeAsync()
    {
        return DisposeAsync(true);
    }

    public ValueTask DisposeAsync(bool graceFul)
    {
        lock (_disposeLock)
            _disposeTask ??= DisposeAsyncCore(graceFul);
        return _disposeTask.Value;
    }

    private async ValueTask DisposeAsyncCore(bool graceFul)
    {
        Connected = false;
        _disposed = true;
        await _hostTcpClientStream.DisposeAsync(graceFul);
        await _tunnelTcpClientStream.DisposeAsync(graceFul);
    }
}