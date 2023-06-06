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
        var totalCacheRead = 0;
        // read directly to user buffer if there is not buffer and it is larger than cache
        while (true)
        {
            if (_cacheRemain == 0)
            {
                if (count >= _cache.Length)
                    return totalCacheRead + await base.ReadAsync(buffer, offset, count, cancellationToken);

                // fill cache
                _cacheRemain = await base.ReadAsync(_cache, 0, _cache.Length, cancellationToken);
                _cacheOffset = 0;
                if (_cacheRemain == 0)
                    return 0; // nothing remained
            }

            // read from cache if there is data is in cache
            // ReSharper disable once MethodHasAsyncOverloadWithCancellation
            var cacheRead = Math.Min(count, _cacheRemain);
            Buffer.BlockCopy(_cache, _cacheOffset, buffer, offset, cacheRead);
            totalCacheRead += cacheRead;
            _cacheOffset += cacheRead;
            _cacheRemain -= cacheRead;
            count -= cacheRead;
            offset += cacheRead;
            if (count == 0)
                return totalCacheRead;
        }
    }
}