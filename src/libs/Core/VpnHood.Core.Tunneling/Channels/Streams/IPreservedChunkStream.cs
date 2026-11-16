namespace VpnHood.Core.Tunneling.Channels.Streams;

internal interface IPreservedChunkStream
{
    int PreserveWriteBufferLength { get; }
    // The writer may overwrite both the reserved prefix and payload, including on failure.
    // Callers refill the buffer before its next use.
    ValueTask WritePreservedAsync(Memory<byte> buffer, CancellationToken cancellationToken);
}