using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity.Probes;

internal static partial class ParityProbes
{
    // megabytes of framing in an unread body, or of blank lines before a request: Kestrel drains them at memory
    // speed, and counts a chunked body's framing and extensions toward its 30 MB limit
    private static IReadOnlyList<Probe> Bulk() => [
        .. new[] { 500_000, 1_000_000, 2_000_000, 4_000_000, 29_000_000, 31_000_000 }.SelectMany(size =>
            BothBulk($"bulk-extension-{size}", () => ChunkedHead + "1;" + new string('a', size) + "\r\nx\r\n" + LastChunk,
                endWaitSeconds: 8)),
        .. new[] { 170_000, 5_200_000 }.SelectMany(count =>
            BothBulk($"bulk-tiny-chunks-{count}",
                () => ChunkedHead + string.Concat(Enumerable.Repeat("1\r\nx\r\n", count)) + LastChunk, endWaitSeconds: 8)),
        .. new[] { 8_192, 2_000_000 }.SelectMany(count =>
            BothBulk($"bulk-blank-lines-{count}", () => string.Concat(Enumerable.Repeat("\r\n", count)) + Second,
                endWaitSeconds: 3)),
        .. BothBulk("bulk-data-29m", () => ChunkedHead + "1ba8140\r\n" + new string('x', 29_000_000) + "\r\n" + LastChunk,
            endWaitSeconds: 3)
    ];
}
