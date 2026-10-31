using System.Buffers;
using System.Text;

namespace VpnHood.Net.Http3;

// RFC 7541 Appendix B, also used by QPACK. A small decoding tree is built once for the process.
// Requests rent only their bounded output buffer; neither the tree nor a buffer lives on each stream.
internal static class Http3QpackHuffman
{
    private static readonly Http3QpackHuffmanNode[] Nodes = CreateTree();

    public static string Decode(ReadOnlySpan<byte> encoded, int maximumLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLength);
        if (encoded.IsEmpty)
            return "";

        // The shortest code is five bits. Use long arithmetic before imposing the decoded-size cap.
        var capacity = (int)Math.Min(maximumLength, (long)encoded.Length * 8 / 5);
        var decoded = ArrayPool<byte>.Shared.Rent(Math.Max(1, capacity));
        try {
            var count = 0;
            var node = 0;
            var pendingBits = 0;
            var pendingValue = 0;
            foreach (var value in encoded) {
                for (var bit = 7; bit >= 0; bit--) {
                    var one = ((value >> bit) & 1) != 0;
                    node = one ? Nodes[node].One : Nodes[node].Zero;
                    if (node < 0)
                        throw new Http3QpackException("The Huffman string contains an invalid code.");
                    pendingBits++;
                    pendingValue = (pendingValue << 1) | (one ? 1 : 0);
                    var symbol = Nodes[node].Symbol;
                    if (symbol < 0)
                        continue;
                    if (symbol == 256)
                        throw new Http3QpackException("An EOS symbol is not permitted inside a Huffman string.");
                    if (count == maximumLength)
                        throw new Http3HeaderLimitException("The decoded field section is too large.");
                    decoded[count++] = (byte)symbol;
                    node = 0;
                    pendingBits = 0;
                    pendingValue = 0;
                }
            }

            // EOS is thirty one-bits. Padding may be only its first zero to seven bits, never a
            // complete EOS, an entire extra byte, or the prefix of a different symbol.
            if (pendingBits > 7 || pendingValue != (1 << pendingBits) - 1)
                throw new Http3QpackException("The Huffman string has invalid padding.");
            return Encoding.Latin1.GetString(decoded, 0, count);
        }
        finally {
            ArrayPool<byte>.Shared.Return(decoded);
        }
    }

    private static Http3QpackHuffmanNode[] CreateTree()
    {
        var nodes = new List<Http3QpackHuffmanNode> { new() };
        for (var symbol = 0; symbol < Http3QpackHuffmanTable.Codes.Length; symbol++) {
            var code = Http3QpackHuffmanTable.Codes[symbol];
            var node = 0;
            for (var bit = Http3QpackHuffmanTable.Lengths[symbol] - 1; bit >= 0; bit--) {
                var one = ((code >> bit) & 1) != 0;
                var next = one ? nodes[node].One : nodes[node].Zero;
                if (next < 0) {
                    next = nodes.Count;
                    nodes.Add(new Http3QpackHuffmanNode());
                    if (one)
                        nodes[node].One = next;
                    else
                        nodes[node].Zero = next;
                }
                node = next;
            }
            nodes[node].Symbol = symbol;
        }
        return nodes.ToArray();
    }
}
