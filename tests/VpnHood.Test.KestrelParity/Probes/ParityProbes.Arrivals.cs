using VpnHood.Test.KestrelParity.Probing;

namespace VpnHood.Test.KestrelParity.Probes;

internal static partial class ParityProbes
{
    // requests that arrive in pieces: a reader that buffers must judge a byte, a line end and a request's start
    // the same way wherever a read happened to end
    private static IReadOnlyList<Probe> Arrivals() => [
        .. BothPieces("arrive-get-bytewise", OneByOne(Get("x"), 10)),
        .. BothPieces("arrive-chunked-bytewise", OneByOne(ChunkedHead + "1;ab\r\nx\r\n0\r\nX-T: a\r\n\r\n" + Second, 5)),
        .. BothPieces("arrive-blank-lines-then-get", OneByOne("\r\n\r\n" + Get("x"), 10)),
        .. BothPieces("arrive-size-bad-byte", [(ChunkedHead + "1", 500), ("x", 500), ("y", 0)]),
        .. BothPieces("arrive-extension-cr-then-lf", [(ChunkedHead + "1;a\r", 500), ("\n", 0), ("x\r\n" + LastChunk, 0)]),
        .. BothPieces("arrive-line-end-split", [(Get("x")[..^1], 500), ("\n", 0)])
    ];
}
