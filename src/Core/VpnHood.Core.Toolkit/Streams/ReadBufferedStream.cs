using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Toolkit.Streams;

/// <summary>
/// Caches read operations for small-sized requests to reduce overhead.
/// Write operations are delegated to the underlying source stream.
/// </summary>
public class ReadBufferedStream : StreamDecoratorAsync
{
    private readonly Memory<byte> _buffer;
    private int _bufferRemain;
    private int _bufferOffset;
    private const int DefaultBufferSize = 1024;

    public override bool CanSeek => false;
    public bool AllowBufferRefill { get; set; } = true;
    public override bool? DataAvailable => _bufferRemain > 0 ? true : base.DataAvailable;

    public ReadBufferedStream(Stream sourceStream, bool leaveOpen, ReadOnlySpan<byte> initData)
        : this(sourceStream, leaveOpen, initData.Length, initData)
    {
        if (initData.Length == 0)
            throw new ArgumentException("Cache data cannot be empty when using this constructor.", nameof(initData));
    }

    public ReadBufferedStream(Stream sourceStream, bool leaveOpen, int bufferSize = DefaultBufferSize,
        ReadOnlySpan<byte> initData = default)
        : base(sourceStream, leaveOpen)
    {
        if (bufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "Cache size must be greater than zero.");

        if (!initData.IsEmpty && initData.Length > bufferSize)
            throw new ArgumentOutOfRangeException(nameof(initData), "Initial cache data exceeds cache size.");

        _buffer = new byte[bufferSize];
        initData.CopyTo(_buffer.Span);
        _bufferRemain = initData.Length;
    }

    public override long Position {
        get => base.Position - _bufferRemain;
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        // read directly to user buffer if there is no buffer, and it is larger than cache
        if (_bufferRemain == 0 && (buffer.Length > _buffer.Length || !AllowBufferRefill))
            return await base.ReadAsync(buffer, cancellationToken).Vhc();

        // fill cache
        if (_bufferRemain == 0 && buffer.Length <= _buffer.Length) {
            _bufferRemain = await base.ReadAsync(_buffer, cancellationToken).Vhc();
            if (_bufferRemain == 0)
                return 0; // end of stream

            _bufferOffset = 0;
        }

        // Warning: if there is data in cache we are not allowed to fill the cache again
        // because it may go to read blocking
        var cacheRead = Math.Min(buffer.Length, _bufferRemain);
        _buffer.Slice(_bufferOffset, cacheRead).CopyTo(buffer);
        _bufferOffset += cacheRead;
        _bufferRemain -= cacheRead;
        return cacheRead;
    }
}