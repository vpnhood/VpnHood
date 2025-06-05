namespace VpnHood.Core.Toolkit.Utils;

/// <summary>
/// Caches read operations for small-sized requests to reduce overhead.
/// Write operations are delegated to the underlying source stream.
/// </summary>
public class ReadCacheStream : AsyncStreamDecorator
{
    private readonly Memory<byte> _cache;
    private int _cacheRemain;
    private int _cacheOffset;

    public override bool CanSeek => false;

    public ReadCacheStream(Stream sourceStream, bool leaveOpen, int cacheSize = 1024, ReadOnlySpan<byte> cacheData = default)
        : base(sourceStream, leaveOpen)
    {
        if (cacheSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(cacheSize), "Cache size must be greater than zero.");

        if (!cacheData.IsEmpty && cacheData.Length > cacheSize)
            throw new ArgumentOutOfRangeException(nameof(cacheData), "Initial cache data exceeds cache size.");

        _cache = new byte[cacheSize];
        cacheData.CopyTo(_cache.Span);
        _cacheRemain = cacheData.Length;
    }

    public override long Position {
        get => base.Position - _cacheOffset;
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken =default)
    {
        // read directly to user buffer if there is no buffer, and it is larger than cache
        if (_cacheRemain == 0 && buffer.Length > _cache.Length)
            return await base.ReadAsync(buffer, cancellationToken).Vhc();

        // fill cache
        if (_cacheRemain == 0 && buffer.Length <= _cache.Length) {
            _cacheRemain = await base.ReadAsync(_cache, cancellationToken).Vhc();
            if (_cacheRemain == 0)
                return 0; // end of stream

            _cacheOffset = 0;
        }

        // Warning: if there is data in cache we are not allowed to fill the cache again
        // because it may go to read blocking
        var cacheRead = Math.Min(buffer.Length, _cacheRemain);
        _cache.Slice(_cacheOffset, cacheRead).CopyTo(buffer);
        _cacheOffset += cacheRead;
        _cacheRemain -= cacheRead;
        return cacheRead;
    }

}