using System.Buffers;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling;

public class StreamCryptor : AsyncStreamDecorator
{
    private readonly BufferCryptor _bufferCryptor;
    private readonly bool _leaveOpen;
    private readonly long _maxCipherCount;
    private readonly Stream _stream;

    private long _readCount;
    private long _writeCount;

    private StreamCryptor(Stream stream, byte[] key, long maxCipherCount,
        bool leaveOpen)
        : base(stream, leaveOpen)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));

        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _bufferCryptor = new BufferCryptor(key);
        _maxCipherCount = maxCipherCount;
        _leaveOpen = leaveOpen;
    }

    public override bool CanSeek => false;


    public static StreamCryptor Create(Stream stream, byte[] key, byte[]? salt = null,
        long maxCipherPos = long.MaxValue,
        bool leaveOpen = false, bool encryptInGivenBuffer = true)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        if (key is null) throw new ArgumentNullException(nameof(key));

        var encKey = key;

        // apply salt if salt exists
        if (salt != null) {
            if (key.Length != salt.Length)
                throw new Exception($"{nameof(key)} length and {nameof(salt)} length is not same.");
            encKey = (byte[])key.Clone();
            for (var i = 0; i < encKey.Length; i++)
                encKey[i] ^= salt[i];
        }

        return new StreamCryptor(stream, encKey, maxCipherPos, leaveOpen);
    }


    public void Decrypt(Span<byte> buffer)
    {
        var cipherCount = (int)Math.Min(buffer.Length, _maxCipherCount - _readCount);
        if (cipherCount > 0) {
            lock (_bufferCryptor)
                _bufferCryptor.Cipher(buffer[..cipherCount], _readCount);
            _readCount += buffer.Length;
        }
    }

    public void Encrypt(Span<byte> buffer)
    {
        var cipherCount = (int)Math.Min(buffer.Length, _maxCipherCount - _writeCount);
        if (cipherCount > 0) {
            lock (_bufferCryptor)
                _bufferCryptor.Cipher(buffer[..cipherCount], _writeCount);
            _writeCount += cipherCount;
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var readCount = await _stream.ReadAsync(buffer, cancellationToken).Vhc();
        Decrypt(buffer.Span[..readCount]);
        return readCount;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        // copy buffer to a shared buffer from memory pool to avoid memory allocation
        using var memoryOwner = MemoryPool<byte>.Shared.Rent(buffer.Length);
        var copyBuffer = memoryOwner.Memory[..buffer.Length]; // warning: the returned memory may be larger than the original buffer
        buffer.Span.CopyTo(copyBuffer.Span);
        Encrypt(copyBuffer.Span);

        // must await to let copyBuffer be disposed
        await _stream.WriteAsync(copyBuffer, cancellationToken);
    }

    public override async ValueTask DisposeAsync()
    {
        lock (_bufferCryptor)
            _bufferCryptor.Dispose();

        if (!_leaveOpen)
            await _stream.DisposeAsync().Vhc();

        await base.DisposeAsync().Vhc();
    }
}