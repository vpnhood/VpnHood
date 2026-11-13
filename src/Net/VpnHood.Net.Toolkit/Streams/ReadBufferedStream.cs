using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Net.Toolkit.Streams;

/// <summary>
/// Provides a stream that buffers small reads of another stream, to read it in fewer, larger reads; writes go to the
/// source as they are.
/// </summary>
/// <remarks>
/// A read larger than the buffer, with the buffer empty, goes to the source directly. A read takes from the buffer while it
/// holds data, and refills it only once it is empty, so a read never waits for more data while some is buffered.
/// </remarks>
public class ReadBufferedStream : StreamDecoratorAsync
{
    private readonly Memory<byte> _buffer;
    private int _bufferRemain;
    private int _bufferOffset;
    private const int DefaultBufferSize = 1024;

    /// <inheritdoc/>
    /// <remarks>It is <see langword="false"/>: the buffer hides the source's position.</remarks>
    public override bool CanSeek => false;

    /// <summary>
    /// Gets or sets a value indicating whether an empty buffer is filled again; otherwise, once it is empty, reads go to
    /// the source directly. The default is <see langword="true"/>.
    /// </summary>
    public bool AllowBufferRefill { get; set; } = true;

    /// <inheritdoc/>
    /// <remarks>It is <see langword="true"/> while the buffer holds data; otherwise, the source tells.</remarks>
    public override bool? DataAvailable => _bufferRemain > 0 ? true : base.DataAvailable;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReadBufferedStream"/> class whose buffer starts with data already read
    /// from the source, and is that long.
    /// </summary>
    /// <param name="sourceStream">The source stream.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave the source open when this stream is disposed.</param>
    /// <param name="initData">The data read before, which reads return first.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="initData"/> is empty.</exception>
    public ReadBufferedStream(Stream sourceStream, bool leaveOpen, ReadOnlySpan<byte> initData)
        : this(sourceStream, leaveOpen, initData.Length, initData)
    {
        if (initData.Length == 0)
            throw new ArgumentException("Cache data cannot be empty when using this constructor.", nameof(initData));
    }

    /// <summary>Initializes a new instance of the <see cref="ReadBufferedStream"/> class.</summary>
    /// <param name="sourceStream">The source stream.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave the source open when this stream is disposed.</param>
    /// <param name="bufferSize">The buffer's size, in bytes. The default is 1024.</param>
    /// <param name="initData">Data read before, which reads return first, or empty.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="bufferSize"/> is not above zero, or <paramref name="initData"/> is longer than it.
    /// </exception>
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

    /// <inheritdoc/>
    /// <remarks>Reading it gives the source's position less the buffered bytes; setting it is not supported.</remarks>
    /// <exception cref="NotSupportedException">The position is set, or the source cannot tell its position.</exception>
    public override long Position {
        get => base.Position - _bufferRemain;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        // what is buffered goes with the stream too
        ObjectDisposedException.ThrowIf(IsDisposed, this);

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