using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Common.JobController;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.Tunneling;

public class TcpProxyChannel : IChannel, IJob
{
    private readonly int _orgStreamReadBufferSize;
    private readonly TcpClientStream _orgTcpClientStream;
    private readonly int _tunnelStreamReadBufferSize;
    private readonly TcpClientStream _tunnelTcpClientStream;
    private const int BufferSizeDefault = 0x1000 * 4; //16k
    private const int BufferSizeMax = 0x14000;
    private bool _disposed;

    public TcpProxyChannel(TcpClientStream orgTcpClientStream, TcpClientStream tunnelTcpClientStream,
        TimeSpan tcpTimeout, int orgStreamReadBufferSize = 0, int tunnelStreamReadBufferSize = 0)
    {
        _orgTcpClientStream = orgTcpClientStream ?? throw new ArgumentNullException(nameof(orgTcpClientStream));
        _tunnelTcpClientStream = tunnelTcpClientStream ?? throw new ArgumentNullException(nameof(tunnelTcpClientStream));
        
        if (orgStreamReadBufferSize == 0) orgStreamReadBufferSize = BufferSizeDefault;
        if (tunnelStreamReadBufferSize == 0) tunnelStreamReadBufferSize = BufferSizeDefault;

        _orgStreamReadBufferSize = orgStreamReadBufferSize is > 0 and <= BufferSizeMax
            ? orgStreamReadBufferSize
            : throw new ArgumentOutOfRangeException($"Value must greater than 0 and less than {BufferSizeMax}", orgStreamReadBufferSize, nameof(orgStreamReadBufferSize));

        _tunnelStreamReadBufferSize = tunnelStreamReadBufferSize is > 0 and <= BufferSizeMax
            ? tunnelStreamReadBufferSize
            : throw new ArgumentOutOfRangeException($"Value must greater than 0 and less than {BufferSizeMax}", tunnelStreamReadBufferSize, nameof(tunnelStreamReadBufferSize));

        // We don't know about client or server delay, so lets pessimistic
        orgTcpClientStream.TcpClient.NoDelay = true;
        tunnelTcpClientStream.TcpClient.NoDelay = true;

        JobSection = new JobSection(tcpTimeout);
        JobRunner.Default.Add(this);
    }

    public JobSection JobSection { get; }

    public event EventHandler<ChannelEventArgs>? OnFinished;
    public bool Connected { get; private set; }
    public long SentByteCount { get; private set; }
    public long ReceivedByteCount { get; private set; }
    public DateTime LastActivityTime { get; private set; } = FastDateTime.Now;

    public async Task Start()
    {
        Connected = true;
        try
        {
            var task1 = CopyToAsync(_tunnelTcpClientStream.Stream, _orgTcpClientStream.Stream, false, _tunnelStreamReadBufferSize, CancellationToken.None); // read
            var task2 = CopyToAsync(_orgTcpClientStream.Stream, _tunnelTcpClientStream.Stream, true, _orgStreamReadBufferSize, CancellationToken.None); //write
            await Task.WhenAny(task1, task2);
        }
        finally
        {
            Dispose();
            OnFinished?.Invoke(this, new ChannelEventArgs(this));
        }
    }

    private static bool IsConnectionValid(Socket socket)
    {
        try
        {
            return !socket.Poll(0, SelectMode.SelectError);
        }
        catch
        {
            return false;
        }
    }

    public Task RunJob()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        CheckTcpStates();
        return Task.CompletedTask;
    }

    private void CheckTcpStates()
    {
        if (IsConnectionValid(_orgTcpClientStream.TcpClient.Client) &&
            IsConnectionValid(_tunnelTcpClientStream.TcpClient.Client))
            return;

        VhLogger.Instance.LogInformation(GeneralEventId.StreamChannel,
            $"Disposing a {VhLogger.FormatTypeName(this)} due to its error state.");

        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Connected = false;
        _orgTcpClientStream.Dispose();
        _tunnelTcpClientStream.Dispose();
    }

    private async Task CopyToAsync(Stream source, Stream destination, bool isSendingOut, int bufferSize,
        CancellationToken cancellationToken)
    {
        try
        {
            await CopyToInternalAsync(source, destination, isSendingOut, bufferSize, cancellationToken);
        }
        catch (Exception ex)
        {
            // Dispose if any side throw an exception
            var message = isSendingOut ? "to" : "from";
            VhLogger.Instance.LogInformation(GeneralEventId.Tcp, ex, $"TcpProxyChannel: Error in copying {message} tunnel.");
            Dispose();
        }
    }

    private async Task CopyToInternalAsync(Stream source, Stream destination, bool isSendingOut, int bufferSize,
        CancellationToken cancellationToken)
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
        while (!cancellationToken.IsCancellationRequested)
        {
            // read from source
            var bytesRead = await source.ReadAsync(readBuffer, 0, readBuffer.Length, cancellationToken);
            if (writeTask != null)
                await writeTask;

            // check end of the stream
            if (bytesRead == 0)
                break;

            // write to destination
            if (writeBuffer != null)
            {
                Array.Copy(readBuffer, writeBuffer, bytesRead);
                writeTask = destination.WriteAsync(writeBuffer, 0, bytesRead, cancellationToken);
            }
            else
            {
                await destination.WriteAsync(readBuffer, 0, bytesRead, cancellationToken);
            }

            // calculate transferred bytes
            if (!isSendingOut)
                ReceivedByteCount += bytesRead;
            else
                SentByteCount += bytesRead;

            // set LastActivityTime as some data delegated
            LastActivityTime = FastDateTime.Now;
        }
    }
}