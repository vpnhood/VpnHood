using System.Text;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

// The chunked HTTP stream, kept for a server behind a front without WebSocket
[TestClass]
public class HttpStreamTest : TestBase
{
    [TestMethod]
    public async Task A_chunks_line_end_that_comes_a_byte_at_a_time_is_read()
    {
        // The line end after a chunk was read with one read of two bytes, which a network that delivers them apart
        // answers with one, and the stream failed
        const string response =
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n3\r\nabc\r\n3\r\ndef\r\n0\r\n\r\n";

        await using var source = new TrickleStream(Encoding.ASCII.GetBytes(response));
        await using var stream = new HttpStream(source, streamId: "trickle", host: "localhost");
        using var read = new MemoryStream();
        await stream.CopyToAsync(read, TestCt);

        Assert.AreEqual("abcdef", Encoding.ASCII.GetString(read.ToArray()));
    }
}
