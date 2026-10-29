using System.Text;
using VpnHood.Core.Server.ProbeResistance;

namespace VpnHood.Test.Tests.Servers;

public partial class WebFrontTest
{
    private const string ChunkedHead = "POST / HTTP/1.1\r\nHost: x\r\nTransfer-Encoding: chunked\r\n\r\n";
    private const string NextHead = "GET /next HTTP/1.1\r\nHost: x\r\n\r\n";

    [TestMethod]
    [DataRow("a:", 400)]
    [DataRow(":80", 400)]
    [DataRow(":", 400)]
    [DataRow("[::]", 400)]
    [DataRow("[::1]:", 400)]
    [DataRow("[1]", 400)]
    [DataRow("[0x1]", 400)]
    [DataRow("[::1%x]", 400)]
    [DataRow("[abc]", 0)]
    [DataRow("[...]", 0)]
    [DataRow("[::1]:80", 0)]
    [DataRow("a:99999", 0)]
    public async Task Host_validation_checks_characters_and_port_digits(string host, int rejection)
    {
        await AssertRead($"GET / HTTP/1.1\r\nHost: {host}\r\n\r\n", rejection);
    }

    [TestMethod]
    [DataRow("ff")]
    [DataRow("61a062")]
    [DataRow("61c3")]
    [DataRow("c0af")]
    [DataRow("eda080")]
    [DataRow("f4908080")]
    [DataRow("636166e9")]
    public async Task Malformed_utf8_is_refused_in_headers_and_trailers(string hex)
    {
        var value = Convert.FromHexString(hex);
        await using var header = new TestDuplexStream([
            .. "GET / HTTP/1.1\r\nHost: x\r\nUser-Agent: "u8, .. value, .. "\r\n\r\n"u8]);
        Assert.IsNull(await WebFront.ReadRequest(header.Http, TestCt));
        StringAssert.StartsWith(Encoding.ASCII.GetString(header.Written), "HTTP/1.1 400 ");

        await using var trailer = new TestDuplexStream([
            .. Encoding.ASCII.GetBytes(ChunkedHead + "0\r\nX-T: "), .. value,
            .. Encoding.ASCII.GetBytes("\r\n\r\n" + NextHead)]);
        var request = await ReadRequest(trailer);
        Assert.AreEqual(WebFrontNext.Close, await WebFront.Unauthorized(trailer.Http, request, TestCt));
    }

    [TestMethod]
    [DataRow("café")]
    [DataRow("😀")]
    public async Task Valid_utf8_is_decoded_without_replacement(string value)
    {
        await using var stream = new TestDuplexStream(Encoding.UTF8.GetBytes(
            $"GET / HTTP/1.1\r\nHost: x\r\nX-A: {value}\r\n\r\n"));
        Assert.AreEqual(value, (await ReadRequest(stream)).Headers["X-A"]);
    }

    [TestMethod]
    [DataRow("close\t,x", true)]
    [DataRow("close\v", true)]
    [DataRow("close ,x", false)]
    [DataRow("close\t", false)] // outer HTAB is removed by header parsing, unlike a tab inside a list
    [DataRow(", ,close,,", false)]
    public async Task Connection_tokens_skip_spaces_and_empty_elements_only(string value, bool keepAlive)
    {
        await using var stream = new TestDuplexStream(Encoding.ASCII.GetBytes(
            $"GET / HTTP/1.1\r\nHost: x\r\nConnection: {value}\r\n\r\n"));
        Assert.AreEqual(keepAlive, (await ReadRequest(stream)).KeepAlive);
        await AssertRead("POST / HTTP/1.0\r\nHost: x\r\nConnection: upgrade\t,x\r\n\r\n", 400);
    }

    [TestMethod]
    [DataRow("chunked,", 0)]
    [DataRow("chunked,,", 0)]
    [DataRow(",chunked", 0)]
    [DataRow("chunked , ,", 0)]
    [DataRow("chunked\v", 400)]
    [DataRow("chunked\t,x", 400)]
    [DataRow(",,", 400)]
    public async Task Transfer_encoding_uses_the_last_nonempty_token(string value, int rejection)
    {
        await AssertRead($"POST / HTTP/1.1\r\nHost: x\r\nTransfer-Encoding: {value}\r\n\r\n", rejection);
    }

    [TestMethod]
    [DataRow("x")]
    [DataRow("1x")]
    [DataRow("000000000")]
    [DataRow("1\r\nxy")]
    [DataRow("1;a\n")]
    [DataRow("1;a\r")]
    [DataRow("0\r\n\u0016\u0003")]
    public async Task Partial_chunk_framing_waits_for_the_next_byte(string body)
    {
        await using var stream = new TestDuplexStream(Encoding.ASCII.GetBytes(ChunkedHead + body), blockAtEnd: true);
        var request = await ReadRequest(stream);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var task = WebFront.Unauthorized(stream.Http, request, stop.Token);
        await AssertWaitingForMore(stream, task, stop);
        StringAssert.StartsWith(Encoding.ASCII.GetString(stream.Written), "HTTP/1.1 401 ");
    }

    [TestMethod]
    public async Task A_tls_prefix_in_an_incomplete_header_waits_for_its_line_end()
    {
        await using var stream = new TestDuplexStream("GET / HTTP/1.1\r\nHost: x\r\n\x16\x03"u8.ToArray(), blockAtEnd: true);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var task = WebFront.ReadRequest(stream.Http, stop.Token);
        await AssertWaitingForMore(stream, task, stop);
        Assert.IsEmpty(stream.Written);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(7)]
    [DataRow(16384)]
    public async Task Buffered_draining_preserves_the_next_app_request_and_its_binary_payload(int readSize)
    {
        const string appHead = "POST /app HTTP/1.1\r\nHost: x\r\nX-ProtocolVersion: 14\r\nContent-Length: 3\r\n\r\n";
        await using var stream = new TestDuplexStream([
            .. Encoding.ASCII.GetBytes("\r\n\n\r" + ChunkedHead + "1;abc\r\nx\r\n0\r\nX-T: v\n\n" + appHead),
            1, 2, 3], maxReadSize: readSize);
        var request = await ReadRequest(stream);
        Assert.AreEqual(WebFrontNext.NextRequest, await WebFront.Unauthorized(stream.Http, request, TestCt));
        Assert.AreEqual("/app", (await ReadRequest(stream)).Target);
        var payload = new byte[3];
        await stream.Http.ReadExactlyAsync(payload, TestCt);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, payload);
        if (readSize == 16384)
            Assert.AreEqual(1, stream.ReadCount, "The request and payload were already in read-ahead.");
    }

    [TestMethod]
    [DataRow("1\r\nx\r\n0\r\n\r\n", 11, true)]
    [DataRow("1\r\nx\r\n0\r\n\r\n", 10, false)]
    [DataRow("1;a=b\r\nx\r\n0\r\n\r\n", 15, true)]
    [DataRow("1;a=b\r\nx\r\n0\r\n\r\n", 14, false)]
    [DataRow("1\r\nx\r\n0\r\nX-T: value\r\n\r\n", 9, true)]
    [DataRow("1\r\nx\r\n0\r\nX-T: value\r\n\r\n", 8, false)]
    public async Task The_chunk_budget_counts_framing_but_keeps_trailer_headers_separate(string body, int limit, bool accepted)
    {
        await using var stream = new TestDuplexStream(Encoding.ASCII.GetBytes(ChunkedHead + body + NextHead), maxReadSize: 1);
        var request = await ReadRequest(stream);
        var reader = new HttpChunkedBody(limit);
        Assert.AreEqual(accepted, await reader.Drain(stream.Http, request, TestCt));
        if (accepted)
            Assert.AreEqual("/next", (await ReadRequest(stream)).Target);
    }

    [TestMethod]
    [DataRow(29_000_000, true)]
    [DataRow(31_000_000, false)]
    public async Task Large_chunk_extensions_are_drained_in_batches(int length, bool accepted)
    {
        await using var stream = new TestDuplexStream(Encoding.ASCII.GetBytes(
            ChunkedHead + "1;" + new string('a', length) + "\r\nx\r\n0\r\n\r\n" + NextHead));
        var request = await ReadRequest(stream);
        Assert.AreEqual(accepted ? WebFrontNext.NextRequest : WebFrontNext.Reset,
            await WebFront.Unauthorized(stream.Http, request, TestCt));
        Assert.IsLessThan(3000, stream.ReadCount);
        if (accepted)
            Assert.AreEqual("/next", (await ReadRequest(stream)).Target);
    }

    [TestMethod]
    public async Task Blank_lines_are_skipped_in_batches_before_a_request()
    {
        await using var stream = new TestDuplexStream(Encoding.ASCII.GetBytes(new string('\n', 4_000_000) + NextHead));
        Assert.AreEqual("/next", (await ReadRequest(stream)).Target);
        Assert.IsLessThan(300, stream.ReadCount);
    }

    [TestMethod]
    public async Task Blank_lines_alone_do_not_start_the_header_deadline()
    {
        await using var stream = new TestDuplexStream("\r\n\n\r"u8.ToArray(), blockAtEnd: true);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var task = WebFront.ReadRequest(stream.Http, stop.Token);
        try {
            await stream.ReadBlocked.Task.WaitAsync(TestCt);
            // Past the 30 s header deadline and its heartbeat, but within the 130 s idle deadline.
            await Task.Delay(TimeSpan.FromSeconds(34), TestCt);
            Assert.IsFalse(task.IsCompleted);
            Assert.IsEmpty(stream.Written);
        }
        finally {
            await stop.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => task);
        }
    }

    [TestMethod]
    public async Task Cancellation_is_observed_even_when_the_body_is_already_buffered()
    {
        await using var stream = new TestDuplexStream(Encoding.ASCII.GetBytes(ChunkedHead + "1;a\r\nx\r\n0\r\n\r\n"));
        var request = await ReadRequest(stream);
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => WebFront.Unauthorized(stream.Http, request, stop.Token));
    }

    private async Task AssertWaitingForMore(TestDuplexStream stream, Task task, CancellationTokenSource stop)
    {
        try {
            Assert.AreSame(stream.ReadBlocked.Task, await Task.WhenAny(task, stream.ReadBlocked.Task).WaitAsync(TestCt));
            Assert.IsFalse(task.IsCompleted);
        }
        finally {
            await stop.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => task);
        }
    }
}
