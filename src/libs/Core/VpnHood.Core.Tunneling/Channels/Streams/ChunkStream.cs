using VpnHood.Net.Toolkit.Streams;

namespace VpnHood.Core.Tunneling.Channels.Streams;

/// <summary>
/// Provides the base of a stream that carries its data in chunks over another stream, so its end is marked and the
/// other stream can go on to carry the next stream.
/// </summary>
public abstract class ChunkStream : StreamDecoratorAsync
{
    internal abstract bool CanReuse { get; }
    internal abstract Task<ChunkStream> CreateReuse();
    internal int ReusedCount { get; }
    internal int ReadChunkCount { get; set; }
    internal int WroteChunkCount { get; set; }
    internal string StreamId { get; set; }


    private protected ChunkStream(Stream sourceStream, string streamId)
        : base(sourceStream, leaveOpen: true)
    {
        StreamId = streamId;
        ReusedCount = 0;
    }

    private protected ChunkStream(Stream sourceStream, string streamId, int reusedCount)
        : base(sourceStream, true)
    {
        StreamId = streamId;
        ReusedCount = reusedCount;
    }
}