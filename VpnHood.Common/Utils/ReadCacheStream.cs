using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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

    public ReadCacheStream(Stream sourceStream, bool keepOpen, int cacheSize = 1024)
        : base(sourceStream, keepOpen)
    {
        _cache = new byte[cacheSize];
    }

    public override long Position
    {
        get => base.Position - _cacheOffset;
        set => throw new NotSupportedException();
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return await base.ReadAsync(buffer, offset, count, cancellationToken);

        // read directly to user buffer if there is not buffer and it is larger than cache
        if (_cacheRemain == 0 && count > _cache.Length)
            return await base.ReadAsync(buffer, offset, count, cancellationToken);

        // fill cache
        if (_cacheRemain == 0 && count <= _cache.Length)
            _cacheRemain = await base.ReadAsync(_cache, 0, _cache.Length, cancellationToken);

        // Warning: if there is data in cache we are not allowed to fill the cache again
        // because it may go to read blocking
        var cacheRead = Math.Min(count, _cacheRemain);
        Buffer.BlockCopy(_cache, _cacheOffset, buffer, offset, cacheRead);
        _cacheOffset += cacheRead;
        _cacheRemain -= cacheRead;
        return cacheRead;
    }
}