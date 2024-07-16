using VpnHood.Common.Utils;

namespace VpnHood.Tunneling.Channels.Streams;

public abstract class ChunkStream : AsyncStreamDecorator
{
    public abstract bool CanReuse { get; }
    public abstract Task<ChunkStream> CreateReuse();
    public int ReusedCount { get; }
    public int ReadChunkCount { get; protected set; }
    public int WroteChunkCount { get; protected set; }
    public string StreamId { get; internal set; }
    public bool PreserveWriteBuffer { get; set; }
    public virtual int PreserveWriteBufferLength => 0;

    protected ChunkStream(Stream sourceStream, string streamId)
        : base(sourceStream, true)
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