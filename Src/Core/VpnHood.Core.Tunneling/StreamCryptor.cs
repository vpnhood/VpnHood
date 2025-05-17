using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling;

public class StreamCryptor : AsyncStreamDecorator
{
    private readonly BufferCryptor _bufferCryptor;
    private readonly bool _leaveOpen;
    private readonly long _maxCipherCount;
    private readonly Stream _stream;
    private readonly bool _encryptInGivenBuffer;

    private long _readCount;
    private long _writeCount;

    private StreamCryptor(Stream stream, byte[] key, long maxCipherCount,
        bool leaveOpen, bool encryptInGivenBuffer)
        : base(stream, leaveOpen)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));

        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _bufferCryptor = new BufferCryptor(key);
        _maxCipherCount = maxCipherCount;
        _leaveOpen = leaveOpen;
        _encryptInGivenBuffer = encryptInGivenBuffer;
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

        return new StreamCryptor(stream, encKey, maxCipherPos, leaveOpen, encryptInGivenBuffer);
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

    public void Encrypt(byte[] buffer, int offset, int count)
    {
        var cipherCount = (int)Math.Min(count, _maxCipherCount - _writeCount);
        if (cipherCount > 0) {
            lock (_bufferCryptor)
                _bufferCryptor.Cipher(buffer[..cipherCount], _writeCount);
            _writeCount += cipherCount;
        }
    }

    //todo: convert to Memory
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer2, CancellationToken cancellationToken = default)
    {
        var buffer = buffer2.ToArray();
        var offset = 0;
        var count = buffer.Length;


        var readCount = await _stream.ReadAsync(buffer, offset, count, cancellationToken).VhConfigureAwait();
        Decrypt(buffer2.Span[..readCount]);
        return readCount;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_encryptInGivenBuffer) {
            Encrypt(buffer, offset, count);
            return _stream.WriteAsync(buffer, offset, count, cancellationToken);
        }

        var copyBuffer = buffer[offset..count];
        Encrypt(copyBuffer, 0, copyBuffer.Length);
        return _stream.WriteAsync(copyBuffer, 0, copyBuffer.Length, cancellationToken);
    }

    public override async ValueTask DisposeAsync()
    {
        lock (_bufferCryptor)
            _bufferCryptor.Dispose();

        if (!_leaveOpen)
            await _stream.DisposeAsync().VhConfigureAwait();

        await base.DisposeAsync().VhConfigureAwait();
    }
}