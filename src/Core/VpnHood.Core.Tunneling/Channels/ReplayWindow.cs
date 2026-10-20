namespace VpnHood.Core.Tunneling.Channels;

// The seqs its channel has opened, in a window below the highest: one opened before, or older than the window, does
// not go in again, so a copy of a datagram cannot hand its packets up twice. A ring of 64-bit words, as WireGuard's
// (RFC 6479): moving up clears only the words it passes, and the window is a word short of the ring, the one being
// filled. Not thread-safe: its channel's receive serializes it
public sealed class ReplayWindow
{
    private const int WordCount = 128; // 8192 bits, as WireGuard's
    public const int Size = (WordCount - 1) * 64;

    private readonly ulong[] _words = new ulong[WordCount];
    private ulong _top; // the highest seq in, plus one: 0 before the first

    // false for a seq that went in before, or one older than the window
    public bool TryAdd(ulong sequenceNumber)
    {
        var position = sequenceNumber + 1;
        if (_top > Size && position < _top - Size)
            return false;

        var index = position / 64;
        if (position > _top) {
            // the words past the old top's are reused, and hold none of the new seqs yet
            var topIndex = _top / 64;
            var count = Math.Min(index - topIndex, WordCount);
            for (var i = 1UL; i <= count; i++)
                _words[(topIndex + i) % WordCount] = 0;

            _top = position;
        }

        ref var word = ref _words[index % WordCount];
        var bit = 1UL << (int)(position % 64);
        if ((word & bit) != 0)
            return false;

        word |= bit;
        return true;
    }
}
