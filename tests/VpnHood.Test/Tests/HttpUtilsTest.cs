using System.Text;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Test.Tests;

// The reading of an HTTP header off a stream, as the client reads its server's answer
[TestClass]
public class HttpUtilsTest : TestBase
{
    // a stream that gives one byte at a time, as a network may
    private sealed class TrickleStream(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            base.ReadAsync(buffer, offset, Math.Min(1, count), cancellationToken);
    }

    [TestMethod]
    public async Task A_header_ends_at_its_blank_line_alone()
    {
        // Four line breaks of any kind ended it: a header with four CRs inside ended there, and the rest of it was
        // left for the body
        const string header = "HTTP/1.1 200 OK\r\nX-Odd: a\r\r\r\rb\r\nX-Last: c\r\n\r\n";
        await using var stream = new TrickleStream(Encoding.ASCII.GetBytes(header + "body"));
        using var headerStream = await HttpUtils.ReadHeadersAsync(stream, TestCt);
        Assert.AreEqual(header, Encoding.ASCII.GetString(headerStream.ToArray()));

        var rest = new byte[4];
        await stream.ReadExactlyAsync(rest, TestCt);
        Assert.AreEqual("body", Encoding.ASCII.GetString(rest));
    }

    [TestMethod]
    public async Task A_connection_closed_before_its_answer_says_so()
    {
        // it was taken for an answer in another format, with the advice to update the app
        await using var stream = new MemoryStream();
        var ex = await Assert.ThrowsExactlyAsync<EndOfStreamException>(() => HttpUtils.ReadResponse(stream, TestCt));
        Assert.DoesNotContain("latest version", ex.Message);
    }

    [TestMethod]
    public async Task A_connection_closed_inside_its_answer_says_so()
    {
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nX-Cut: a"));
        await Assert.ThrowsExactlyAsync<EndOfStreamException>(() => HttpUtils.ReadResponse(stream, TestCt));
    }
}
