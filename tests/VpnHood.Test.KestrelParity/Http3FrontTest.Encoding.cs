using System.Text;

namespace VpnHood.Test.KestrelParity;

public partial class Http3FrontTest
{
    [TestMethod]
    [TestCategory("Http3Encoding")]
    [DataRow("invalid-ordinary-value")]
    [DataRow("invalid-path")]
    [DataRow("unicode-path")]
    [DataRow("unicode-path-with-query")]
    [DataRow("unicode-ordinary-value")]
    public async Task Utf8_header_octets_match_the_reference(string name)
    {
        List<(string Name, byte[] Value)> fields = [
            (":method", "GET"u8.ToArray()), (":scheme", "https"u8.ToArray()),
            (":path", "/"u8.ToArray()), (":authority", Encoding.ASCII.GetBytes(Servers.HostName))
        ];
        switch (name) {
            case "invalid-ordinary-value": fields.Add(("x-test", [0x80])); break;
            case "invalid-path": fields[2] = (":path", [0x2f, 0x80]); break;
            case "unicode-path": fields[2] = (":path", [0x2f, 0xc3, 0xa9]); break;
            case "unicode-path-with-query": fields[2] = (":path", [0x2f, 0xc3, 0xa9, 0x3f, 0x71]); break;
            case "unicode-ordinary-value": fields.Add(("x-test", [0xc3, 0xa9])); break;
            default: throw new ArgumentOutOfRangeException(nameof(name));
        }
        var request = BuildOctetRequest(fields);
        if (name is "unicode-path" or "unicode-ordinary-value") {
            await AssertResponseMatches(name, request, [], null, TestContext.CancellationToken);
        }
        else {
            // Record the reference's error code and scope; do not assume all non-ASCII paths fail.
            await AssertRequestMatches(name, request, fragmented: false, withholdFin: false, sendFin: true,
                TestContext.CancellationToken);
        }
    }

    private static byte[] BuildOctetRequest(IEnumerable<(string Name, byte[] Value)> fields)
    {
        // Encoding probes must preserve the supplied octets. The text helper intentionally uses
        // ASCII, which would replace malformed UTF-8 or Unicode with '?' before it reached the server.
        using var block = new MemoryStream();
        block.Write(new byte[] { 0, 0 });
        foreach (var (name, value) in fields) {
            var nameBytes = Encoding.ASCII.GetBytes(name);
            WriteQpackInteger(block, 0x20, 3, nameBytes.Length);
            block.Write(nameBytes);
            WriteQpackInteger(block, 0, 7, value.Length);
            block.Write(value);
        }
        return Frame(1, block.ToArray());
    }
}
