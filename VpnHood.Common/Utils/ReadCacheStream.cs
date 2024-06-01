namespace VpnHood.Common.Utils;

/// <summary>
/// Add cache to ReadAsync operations when caller request small amount of data.
/// All write delegate to the source stream
/// </summary>
public class ReadCacheStream : AsyncStreamDecorator
{
    private readonly byte[] _cache;
    private int _cacheRemain;
    private int _cacheOffset;

    public override bool CanSeek => false;
    public bool DataAvailableInCache => _cacheRemain > 0;

    public ReadCacheStream(Stream sourceStream, bool leaveOpen, int cacheSize = 1024, byte[]? cacheData = null)
        : base(sourceStream, leaveOpen)
    {
        _cache = new byte[cacheSize];
        if (cacheData != null)
        {
            cacheData.CopyTo(_cache, 0);
            _cacheRemain = cacheData.Length;
        }
    }

    public override long Position
    {
        get => base.Position - _cacheOffset;
        set => throw new NotSupportedException();
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        // read directly to user buffer if there is no buffer, and it is larger than cache
        if (_cacheRemain == 0 && count > _cache.Length)
            return await base.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);

        // fill cache
        if (_cacheRemain == 0 && count <= _cache.Length)
        {
            _cacheRemain = await base.ReadAsync(_cache, 0, _cache.Length, cancellationToken).ConfigureAwait(false);
            _cacheOffset = 0;

        }

        // Warning: if there is data in cache we are not allowed to fill the cache again
        // because it may go to read blocking
        var cacheRead = Math.Min(count, _cacheRemain);
        Buffer.BlockCopy(_cache, _cacheOffset, buffer, offset, cacheRead);
        _cacheOffset += cacheRead;
        _cacheRemain -= cacheRead;
        return cacheRead;
    }
}