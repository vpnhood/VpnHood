using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling.Channels.Streams;

public abstract class ChunkStream : AsyncStreamDecorator
{
    protected bool AllowReuse = true;

    public abstract bool CanReuse { get; }
    public abstract Task<ChunkStream> CreateReuse();
    public void PreventReuse() => AllowReuse = false;
    public int ReusedCount { get; }
    public int ReadChunkCount { get; protected set; }
    public int WroteChunkCount { get; protected set; }
    public string StreamId { get; internal set; }


    protected ChunkStream(Stream sourceStream, string streamId)
        : base(sourceStream, leaveOpen: true)
    {
        StreamId = streamId;
        ReusedCount = 0;
    }

    protected ChunkStream(Stream sourceStream, string streamId, int reusedCount)
        : base(sourceStream, true)
    {
        StreamId = streamId;
        ReusedCount = reusedCount;
    }
}