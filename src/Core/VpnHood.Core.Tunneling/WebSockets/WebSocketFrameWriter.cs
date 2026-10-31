using System.Buffers;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Tunneling.WebSockets;

// Owns every write to one logical WebSocket stream. It never closes the transport: the stream owner
// decides whether to reuse it after data/control writes and optional padding have finished.
internal sealed class WebSocketFrameWriter(Stream sourceStream, bool isServer, bool useStandardMasking)
{
    public const int MaximumHeaderLength = 14;

    // Only gap cover can write alongside data. Once it ends, packet and later data writes need no gate.
    private SemaphoreSlim? _writeGate;
    private readonly Memory<byte> _headerBuffer = new byte[MaximumHeaderLength];
    private InitialStreamPadding? _initialPadding;
    private bool _stopped;
    private bool _disposed;

    public int WroteChunkCount { get; private set; }
    public Exception? Failure { get; private set; }
    public Task PaddingCompletion => _initialPadding?.Completion ?? Task.CompletedTask;

    public void EnableInitialPadding()
    {
        ObjectDisposedException.ThrowIf(_stopped || _disposed, this);
        if (!useStandardMasking)
            throw new InvalidOperationException("Initial padding requires negotiated WebSocket masking.");
        if (WroteChunkCount != 0)
            throw new InvalidOperationException("Initial padding must be enabled before the first stream write.");
        _initialPadding ??= new InitialStreamPadding(this);
    }

    // The first padded data write has finished before cover starts; no earlier write needs this gate.
    public void StartCover() => Volatile.Write(ref _writeGate, new SemaphoreSlim(1, 1));

    // Publish only after the last cover write released its gate; a captured gate remains valid for
    // any data write already waiting on it, while subsequent writes can bypass it.
    public void FinishCover() => Volatile.Write(ref _writeGate, null);

    public void Stop()
    {
        _stopped = true;
        _initialPadding?.Stop();
    }

    public async ValueTask Write(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var writeGate = Volatile.Read(ref _writeGate);
        if (writeGate != null)
            await writeGate.WaitAsync(cancellationToken).Vhc();
        try {
            ObjectDisposedException.ThrowIf(_stopped || _disposed, this);
            if (data.IsEmpty)
                await sourceStream.WriteAsync(data, cancellationToken).Vhc();
            else if (!await TryWritePadded(data, cancellationToken).Vhc())
                await WriteBinary(data, cancellationToken).Vhc();
        }
        finally {
            writeGate?.Release();
        }
    }

    public async ValueTask WritePreserved(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Length < MaximumHeaderLength)
            throw new ArgumentException("Buffer length is less than required preserved header length.", nameof(buffer));

        var writeGate = Volatile.Read(ref _writeGate);
        if (writeGate != null)
            await writeGate.WaitAsync(cancellationToken).Vhc();
        try {
            ObjectDisposedException.ThrowIf(_stopped || _disposed, this);
            var data = buffer[MaximumHeaderLength..];
            if (data.IsEmpty)
                await sourceStream.WriteAsync(ReadOnlyMemory<byte>.Empty, cancellationToken).Vhc();
            else if (!await TryWritePadded(data, cancellationToken).Vhc())
                await WriteBinaryPreserved(buffer, cancellationToken).Vhc();
        }
        finally {
            writeGate?.Release();
        }
    }

    private ValueTask<bool> TryWritePadded(ReadOnlyMemory<byte> data, CancellationToken cancellationToken) =>
        _initialPadding is { CanPadData: true } padding
            ? padding.TryWrite(data, cancellationToken)
            : ValueTask.FromResult(false);

    private async ValueTask WriteBinaryPreserved(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var header = BuildHeader(_headerBuffer.Span, buffer.Length - MaximumHeaderLength);
        var finalBuffer = buffer[(MaximumHeaderLength - header.Length)..];
        header.CopyTo(finalBuffer.Span);

        // The request's masking offer controls payload masking; MASK alone also appears on legacy frames.
        if (!isServer && useStandardMasking)
            WebSocketUtils.MaskPayload(buffer.Span[MaximumHeaderLength..], header[^4..], 0);

        await sourceStream.WriteAsync(finalBuffer, cancellationToken).Vhc();
        WroteChunkCount++;
    }

    private async ValueTask WriteBinary(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var length = MaximumHeaderLength + data.Length;
        using var memoryOwner = MemoryPool<byte>.Shared.Rent(length);
        var buffer = memoryOwner.Memory[..length];
        data.CopyTo(buffer[MaximumHeaderLength..]);
        await WriteBinaryPreserved(buffer, cancellationToken).Vhc();
    }

    // Combining both frames into one source write lets padding change the outer TLS record containing
    // real data. The caller holds the gate for as long as gap cover can still write.
    public async ValueTask WritePaddedData(ReadOnlyMemory<byte> data, int paddingLength,
        CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(data.Length + paddingLength + 2 * MaximumHeaderLength);
        try {
            var length = EncodeFrame(rented, data.Span, paddingLength: 0, opcode: 2);
            length += EncodeFrame(rented.AsSpan(length), ReadOnlySpan<byte>.Empty, paddingLength, opcode: 1);
            await sourceStream.WriteAsync(rented.AsMemory(0, length), cancellationToken).Vhc();
            WroteChunkCount++;
        }
        finally {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public async Task<bool> TryWriteCover(InitialStreamPadding padding, CancellationToken cancellationToken)
    {
        // Cover skips a busy writer; it must never queue ahead of real traffic.
        var writeGate = Volatile.Read(ref _writeGate);
        if (writeGate == null)
            return false;
        if (!writeGate.Wait(0))
            return true;
        try {
            if (_stopped || _disposed)
                return false;
            var paddingLength = padding.TakePaddingLength(0, cover: true);
            if (paddingLength == 0)
                return false;
            var rented = ArrayPool<byte>.Shared.Rent(paddingLength + MaximumHeaderLength);
            try {
                var length = EncodeFrame(rented, ReadOnlySpan<byte>.Empty, paddingLength, opcode: 1);
                await sourceStream.WriteAsync(rented.AsMemory(0, length), cancellationToken).Vhc();
            }
            finally {
                ArrayPool<byte>.Shared.Return(rented);
            }
            return true;
        }
        catch (OperationCanceledException ex) {
            // A canceled cover frame may be partial; stop later data and forbid transport reuse.
            _stopped = true;
            Failure ??= ex;
            throw;
        }
        catch (Exception ex) {
            _stopped = true;
            Failure ??= ex;
            return false;
        }
        finally {
            writeGate.Release();
        }
    }

    private int EncodeFrame(Span<byte> buffer, ReadOnlySpan<byte> data, int paddingLength, byte opcode)
    {
        var payloadLength = data.Length + paddingLength;
        var header = BuildHeader(buffer[..MaximumHeaderLength], payloadLength);
        header[0] = (byte)(0x80 | opcode);
        var payload = buffer.Slice(header.Length, payloadLength);
        if (paddingLength == 0)
            data.CopyTo(payload);
        else {
            Random.Shared.NextBytes(payload);
            for (var i = 0; i < payload.Length; i++)
                payload[i] = (byte)(' ' + payload[i] % 95); // printable ASCII is valid UTF-8 text
        }
        if (!isServer && useStandardMasking)
            WebSocketUtils.MaskPayload(payload, header[^4..], 0);
        return header.Length + payloadLength;
    }

    private Span<byte> BuildHeader(Span<byte> buffer, int payloadLength)
    {
        if (isServer)
            return WebSocketUtils.BuildWebSocketFrameHeader(buffer, payloadLength);
        Span<byte> key = stackalloc byte[4];
        Random.Shared.NextBytes(key);
        return WebSocketUtils.BuildWebSocketFrameHeader(buffer, payloadLength, key);
    }

    public async ValueTask WriteTerminator(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await WriteBinary(ReadOnlyMemory<byte>.Empty, cancellationToken).Vhc();
    }

    public async ValueTask WriteClose(ushort closeStatus, CancellationToken cancellationToken)
    {
        Stop();
        _disposed = true;
        await PaddingCompletion.WaitAsync(cancellationToken).Vhc();
        await sourceStream.WriteAsync(WebSocketUtils.BuildServerCloseFrame(closeStatus), cancellationToken).Vhc();
        await sourceStream.FlushAsync(cancellationToken).Vhc();
    }

    // The transport owner ends pending native writes. Their rented buffers remain owned until the
    // writes finish. A captured cover gate stays alive until any pending data writer releases it.
    public void Dispose()
    {
        _disposed = true;
        Stop();
        _initialPadding?.Dispose();
    }
}
