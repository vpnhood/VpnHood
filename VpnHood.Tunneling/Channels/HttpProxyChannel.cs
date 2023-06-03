using System;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Common.JobController;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Tunneling.ClientStreams;

namespace VpnHood.Tunneling.Channels;

public class HttpProxyChannel : IChannel, IJob
{
    private readonly int _orgStreamBufferSize;
    private readonly TcpClientStream _orgTcpClientStream;
    private readonly int _tunnelStreamBufferSize;
    private readonly TcpClientStream _tunnelTcpClientStream;
    private const int BufferSizeDefault = 0x1000 * 4; //16k
    private const int BufferSizeMax = 0x14000;
    private const int BufferSizeMin = 0x1000;
    private bool _disposed;

    public JobSection JobSection { get; }
    public event EventHandler<ChannelEventArgs>? OnFinished;
    public bool IsClosePending => false;
    public bool Connected { get; private set; }
    public Traffic Traffic { get; } = new();
    public DateTime LastActivityTime { get; private set; } = FastDateTime.Now;

    public HttpProxyChannel(TcpClientStream orgTcpClientStream, TcpClientStream tunnelTcpClientStream,
        TimeSpan tcpTimeout, int? orgStreamBufferSize = BufferSizeDefault, int? tunnelStreamBufferSize = BufferSizeDefault)
    {
        _orgTcpClientStream = orgTcpClientStream ?? throw new ArgumentNullException(nameof(orgTcpClientStream));
        _tunnelTcpClientStream = tunnelTcpClientStream ?? throw new ArgumentNullException(nameof(tunnelTcpClientStream));

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

        // We don't know about client or server delay, so lets pessimistic
        orgTcpClientStream.TcpClient.NoDelay = true;
        tunnelTcpClientStream.TcpClient.NoDelay = true;

        JobSection = new JobSection(tcpTimeout);
        JobRunner.Default.Add(this);
    }

    public async Task Start()
    {
        Connected = true;
        try
        {
            var task1 = CopyToAsync(_tunnelTcpClientStream.Stream, _orgTcpClientStream.Stream, false, _tunnelStreamBufferSize, CancellationToken.None); // read
            var task2 = CopyToAsync(_orgTcpClientStream.Stream, _tunnelTcpClientStream.Stream, true, _orgStreamBufferSize, CancellationToken.None); //write
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

        VhLogger.Instance.LogInformation(GeneralEventId.TcpProxyChannel,
            $"Disposing a {VhLogger.FormatType(this)} due to its error state.");

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
            if (!_disposed)
            {
                var message = isSendingOut ? "to" : "from";
                VhLogger.Instance.LogInformation(GeneralEventId.Tcp, ex, $"TcpProxyChannel: Error in copying {message} tunnel.");
                Dispose();
            }
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
                Traffic.Received += bytesRead;
            else
                Traffic.Sent += bytesRead;

            // set LastActivityTime as some data delegated
            LastActivityTime = FastDateTime.Now;
        }
    }

    private async Task CopyToHttpAsync(Stream source, Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        // Microsoft Stream Source Code:
        // We pick a value that is the largest multiple of 4096 that is still smaller than the large object heap threshold (85K).
        // The CopyTo/CopyToAsync buffer is short-lived and is likely to be collected at Gen0, and it offers a significant
        // improvement in Copy performance.
        // 0x14000 recommended by microsoft for copying buffers
        if (bufferSize > BufferSizeMax)
            throw new ArgumentException($"Buffer is too big, maximum supported size is {BufferSizeMax}",
                nameof(bufferSize));

        // <<----------------- the MOST memory consuming in the APP! >> ----------------------
        var buffer = new byte[bufferSize];
        var bytesReadBuffer = new byte[10]; // Maximum number of digits in an int
        var chunkFooterBytes = "\r\n"u8.ToArray();
        var chunkHeaderBytes = new byte[bufferSize + 4]; // Maximum chunk header size

        while (!cancellationToken.IsCancellationRequested)
        {
            var bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (bytesRead == 0)
                break;

            // Build the chunk header
            var bytesReadSize = Encoding.ASCII.GetBytes(bytesRead.ToString("X"), bytesReadBuffer);
            Buffer.BlockCopy(bytesReadBuffer, 0, chunkHeaderBytes, 0, bytesReadSize);
            chunkHeaderBytes[bytesReadSize] = (byte)'\r';
            chunkHeaderBytes[bytesReadSize + 1] = (byte)'\n';

            // Write chunk header to the destination
            await destination.WriteAsync(chunkHeaderBytes, 0, bytesReadSize + 2, cancellationToken);

            // Write chunk data to the destination
            await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken);

            // Write chunk footer (empty line) to the destination
            await destination.WriteAsync(chunkFooterBytes, 0, chunkFooterBytes.Length, cancellationToken);

            // calculate transferred bytes
            Traffic.Sent += bytesRead;

            // set LastActivityTime as some data delegated
            LastActivityTime = FastDateTime.Now;

        }

        // Write the last chunk indicating the end of the response
        var lastChunkBytes = "0\r\n\r\n"u8.ToArray();
        await destination.WriteAsync(lastChunkBytes, 0, lastChunkBytes.Length, cancellationToken);
    }

    private static async Task<int> ReadChunkHeaderAsync(Stream stream, byte[] buffer, byte[] chunkHeaderBuffer, CancellationToken cancellationToken)
    {
        var index = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var bytesRead = await stream.ReadAsync(buffer, index, 1, cancellationToken);
            if (bytesRead == 0)
                return 0;

            if (buffer[index] == '\r')
            {
                bytesRead = await stream.ReadAsync(buffer, index, 1, cancellationToken);
                if (bytesRead == 0)
                    return 0;

                if (buffer[index] == '\n')
                    break;
            }

            chunkHeaderBuffer[index] = buffer[index];
            index++;

            // The chunk header buffer is already at its maximum size, throw an exception or handle the situation accordingly
            if (index == chunkHeaderBuffer.Length)
                throw new InvalidOperationException("Chunk header exceeds the maximum size");
        }

        return index;
    }

    private static async Task CopyFromHttpAsync(Stream source, Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        var buffer = new byte[bufferSize];
        var chunkHeaderBuffer = new byte[bufferSize];

        while (true)
        {
            var headerLength = await ReadChunkHeaderAsync(source, buffer, chunkHeaderBuffer, cancellationToken);
            if (headerLength == 0)
                break;

            var chunkSizeHex = Encoding.ASCII.GetString(chunkHeaderBuffer, 0, headerLength);
            if (!int.TryParse(chunkSizeHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var chunkSize))
                throw new InvalidDataException("Invalid chunk size.");

            if (chunkSize == 0)
                break;

            var totalBytesRead = 0;
            while (totalBytesRead < chunkSize)
            {
                var remainingBytes = chunkSize - totalBytesRead;
                var readBytes = await source.ReadAsync(buffer, 0, Math.Min(buffer.Length, remainingBytes), cancellationToken);
                if (readBytes == 0)
                    throw new InvalidDataException("Unexpected end of stream.");

                await destination.WriteAsync(buffer, 0, readBytes, cancellationToken);
                totalBytesRead += readBytes;
            }

            // Read and discard the chunk footer
            _ = await source.ReadAsync(buffer, 0, 2, cancellationToken);
        }
    }

}