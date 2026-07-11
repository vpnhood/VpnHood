namespace VpnHood.Core.Tunneling.Channels.Streams;

public interface IPreservedChunkStream
{
    int PreserveWriteBufferLength { get; }
    ValueTask WritePreservedAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
}