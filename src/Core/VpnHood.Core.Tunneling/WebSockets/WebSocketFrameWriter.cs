using System.Buffers;
using System.Security.Cryptography;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Tunneling.WebSockets;

// Owns every write to one logical WebSocket stream. It never closes the transport: the stream owner
// decides whether to reuse it after data/control writes and optional padding have finished.
internal sealed class WebSocketFrameWriter(Stream sourceStream, bool isServer, bool useStandardMasking)
{
    public const int MaximumHeaderLength = 14;

    // Data, pong replies and gap cover share one gate, so their frames cannot interleave. This is the
    // hot stream-write path; SemaphoreSlim avoids allocating a lock scope for each packet-channel write.
    private readonly SemaphoreSlim _writeGate = new(1, 1);
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

    // Used when the connection owner closes the transport without a logical-stream handshake.
    public void StopInitialPadding() => _initialPadding?.Dispose();

    public void Stop()
    {
        _stopped = true;
        _initialPadding?.Stop();
    }

    public async ValueTask Write(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).Vhc();
        try {
            ObjectDisposedException.ThrowIf(_stopped || _disposed, this);
            if (data.IsEmpty)
                await sourceStream.WriteAsync(data, cancellationToken).Vhc();
            else if (!await TryWritePadded(data, cancellationToken).Vhc())
                await WriteBinary(data, cancellationToken).Vhc();
        }
        finally {
            _writeGate.Release();
        }
    }

    public async ValueTask WritePreserved(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Length < MaximumHeaderLength)
            throw new ArgumentException("Buffer length is less than required preserved header length.", nameof(buffer));

        await _writeGate.WaitAsync(cancellationToken).Vhc();
        try {
            ObjectDisposedException.ThrowIf(_stopped || _disposed, this);
            var data = buffer[MaximumHeaderLength..];
            if (data.IsEmpty)
                await sourceStream.WriteAsync(ReadOnlyMemory<byte>.Empty, cancellationToken).Vhc();
            else if (!await TryWritePadded(data, cancellationToken).Vhc())
                await WriteBinaryPreserved(buffer, cancellationToken).Vhc();
        }
        finally {
            _writeGate.Release();
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
        var headerLength = header.Length;

        // Compatibility (2026-10): existing peers, including early protocol 15 builds, set MASK but
        // leave payloads unchanged. Actual masking requires agreement in the upgrade response.
        if (!isServer && useStandardMasking)
            WebSocketUtils.MaskPayload(buffer.Span[MaximumHeaderLength..], header[^4..], 0);

        // The caller owns this buffer. Restore its payload even if a canceled write prevents reuse.
        try {
            await sourceStream.WriteAsync(finalBuffer, cancellationToken).Vhc();
        }
        finally {
            if (!isServer && useStandardMasking)
                WebSocketUtils.MaskPayload(buffer.Span[MaximumHeaderLength..],
                    finalBuffer.Span.Slice(headerLength - 4, 4), 0);
        }
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

    // Called by the padding policy while Write/WritePreserved holds the gate. Combining these frames
    // into one source write lets padding change the outer TLS record containing the real data.
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

    public async Task TryWriteCover(InitialStreamPadding padding, CancellationToken cancellationToken)
    {
        // Cover skips a busy writer; it must never queue ahead of real traffic.
        if (!_writeGate.Wait(0))
            return;
        try {
            if (_stopped || _disposed)
                return;
            var paddingLength = padding.TakePaddingLength(0, cover: true);
            if (paddingLength == 0)
                return;
            var rented = ArrayPool<byte>.Shared.Rent(paddingLength + MaximumHeaderLength);
            try {
                var length = EncodeFrame(rented, ReadOnlySpan<byte>.Empty, paddingLength, opcode: 1);
                await sourceStream.WriteAsync(rented.AsMemory(0, length), cancellationToken).Vhc();
            }
            finally {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
        catch (OperationCanceledException ex) {
            // A partial cover frame is fatal to reuse. Expose the failure to the stream owner.
            Failure ??= ex;
            throw;
        }
        catch (Exception ex) {
            Failure ??= ex;
        }
        finally {
            _writeGate.Release();
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
        RandomNumberGenerator.Fill(key);
        return WebSocketUtils.BuildWebSocketFrameHeader(buffer, payloadLength, key);
    }

    public async ValueTask WritePong(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).Vhc();
        try {
            // Graceful draining can still receive pings after data writes have stopped.
            if (_disposed)
                return;
            var frame = new byte[MaximumHeaderLength + payload.Length];
            var header = BuildHeader(frame, payload.Length);
            header[0] = 0x8a;
            payload.CopyTo(frame.AsMemory(header.Length));
            if (!isServer)
                WebSocketUtils.MaskPayload(frame.AsSpan(header.Length, payload.Length), header[^4..], 0);
            await sourceStream.WriteAsync(frame.AsMemory(0, header.Length + payload.Length), cancellationToken).Vhc();
        }
        finally {
            _writeGate.Release();
        }
    }

    public async ValueTask WriteTerminator(CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).Vhc();
        try {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await WriteBinary(ReadOnlyMemory<byte>.Empty, cancellationToken).Vhc();
        }
        finally {
            _writeGate.Release();
        }
    }

    public async ValueTask WriteClose(ushort closeStatus, CancellationToken cancellationToken)
    {
        Stop();
        _disposed = true; // no further data or pong frames after a connection close
        await _writeGate.WaitAsync(cancellationToken).Vhc();
        try {
            await sourceStream.WriteAsync(WebSocketUtils.BuildServerCloseFrame(closeStatus), cancellationToken).Vhc();
            await sourceStream.FlushAsync(cancellationToken).Vhc();
        }
        finally {
            _writeGate.Release();
        }
    }

    // The transport owner ends pending native writes. Their rented buffers remain owned until the
    // writes finish. Keep the gate alive too: a pending writer can still release it after this close.
    public void Dispose()
    {
        _disposed = true;
        Stop();
        _initialPadding?.Dispose();
    }
}
