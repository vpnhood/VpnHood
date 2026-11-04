namespace VpnHood.Test.Providers;

internal sealed class Http3GatedRequestStream(byte[] headers, byte[] tail, Http3RequestTestGate gate,
    bool holdTailUntilResponse = false, int responseReleaseBytes = int.MaxValue, int maximumRead = int.MaxValue)
    : MemoryStream(headers)
{
    public int TailBytesProvided { get; private set; }
    public bool PendingReadEnded { get; private set; }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (Position < Length)
            return await base.ReadAsync(buffer, cancellationToken);
        await gate.WaitForApplication(cancellationToken);
        if (holdTailUntilResponse)
            await gate.WaitForResponse(cancellationToken);
        else if (TailBytesProvided < tail.Length) {
            var count = Math.Min(Math.Min(buffer.Length, maximumRead), tail.Length - TailBytesProvided);
            tail.AsMemory(TailBytesProvided, count).CopyTo(buffer);
            TailBytesProvided += count;
            if (TailBytesProvided >= responseReleaseBytes)
                gate.InputConsumed();
            return count;
        }
        else
            gate.InputConsumed();

        try {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        finally {
            // Model native completion touching its destination after its cancellation signal.
            buffer.Span[0] = 0x7e;
            PendingReadEnded = true;
        }
    }
}
