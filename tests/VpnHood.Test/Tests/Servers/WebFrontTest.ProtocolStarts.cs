using System.Text;
using VpnHood.Core.Server.ProbeResistance;

namespace VpnHood.Test.Tests.Servers;

public partial class WebFrontTest
{
    [TestMethod]
    [DataRow("\0")]
    [DataRow("G\0")]
    [DataRow("GET /\0")]
    [DataRow("GET / HTTP/1.1\0")]
    [DataRow("\r\nG\0")]
    [DataRow("\u0016\u0003\u0001\0")]
    public async Task A_zero_byte_rejects_an_incomplete_request_line(string request)
    {
        await using var stream = new TestDuplexStream(Encoding.ASCII.GetBytes(request), blockAtEnd: true, maxReadSize: 1);
        Assert.IsNull(await WebFront.ReadRequest(stream.Http, TestCt));
        StringAssert.StartsWith(Encoding.ASCII.GetString(stream.Written), "HTTP/1.1 400 ");
        Assert.IsFalse(stream.ReadBlocked.Task.IsCompleted, "The zero must be judged without another read.");
    }

    [TestMethod]
    [DataRow(8191, true, 400)]
    [DataRow(8192, true, 414)]
    [DataRow(8193, true, 414)]
    [DataRow(8192, false, 414)]
    public async Task The_request_line_limit_bounds_the_early_zero_check(int prefixLength, bool appendZero,
        int rejection)
    {
        var input = Encoding.ASCII.GetBytes(new string('A', prefixLength) + (appendZero ? "\0" : ""));
        foreach (var maxReadSize in new[] { 1, 16384 }) {
            await using var stream = new TestDuplexStream(input, blockAtEnd: true, maxReadSize: maxReadSize);
            Assert.IsNull(await WebFront.ReadRequest(stream.Http, TestCt));
            StringAssert.StartsWith(Encoding.ASCII.GetString(stream.Written), $"HTTP/1.1 {rejection} ");
            Assert.IsFalse(stream.ReadBlocked.Task.IsCompleted, "The line limit needs no further read.");
        }
    }

    [TestMethod]
    public async Task A_zero_byte_on_a_later_request_line_is_rejected_immediately()
    {
        await using var stream = new TestDuplexStream(Encoding.ASCII.GetBytes(NextHead + "G\0"), blockAtEnd: true);
        var request = await ReadRequest(stream);
        Assert.AreEqual(WebFrontNext.NextRequest, await WebFront.Unauthorized(stream.Http, request, TestCt));
        Assert.IsNull(await WebFront.ReadRequest(stream.Http, TestCt));
        StringAssert.Contains(Encoding.ASCII.GetString(stream.Written), "HTTP/1.1 400 ");
        Assert.IsFalse(stream.ReadBlocked.Task.IsCompleted);
    }

    [TestMethod]
    [DataRow("\u0016\u0003")]
    [DataRow("\r\n\u0016\u0003")]
    [DataRow("GET / HTTP/1.1\r\nHost: x\r\nX-A: \0")]
    public async Task An_incomplete_line_without_a_request_line_zero_keeps_waiting(string request)
    {
        await using var stream = new TestDuplexStream(Encoding.ASCII.GetBytes(request), blockAtEnd: true, maxReadSize: 1);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestCt);
        var task = WebFront.ReadRequest(stream.Http, stop.Token);
        await AssertWaitingForMore(stream, task, stop);
        Assert.IsEmpty(stream.Written);
    }

    [TestMethod]
    [DataRow("PRI * HTTP/2.0\r\n", true, true, true)]
    [DataRow("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n", true, true, true)]
    [DataRow("PRI * HTTP/2.0\r\n", false, false, false)]
    [DataRow("\r\nPRI * HTTP/2.0\r\n", true, false, true)]
    [DataRow("\nPRI * HTTP/2.0\r\n", true, false, true)]
    [DataRow("PRI * HTTP/2.0\n", true, false, false)]
    [DataRow("pri * HTTP/2.0\r\n", true, false, false)]
    [DataRow("PRI / HTTP/2.0\r\n", true, false, false)]
    public async Task Only_the_exact_first_plaintext_preface_gets_http2_goaway(string request, bool firstPlaintext,
        bool goAway, bool bytewiseGoAway)
    {
        foreach (var maxReadSize in new[] { 1, 16384 }) {
            await using var stream = new TestDuplexStream(Encoding.ASCII.GetBytes(request), blockAtEnd: true,
                maxReadSize: maxReadSize);
            Assert.IsNull(await WebFront.ReadRequest(stream.Http, TestCt, allowHttp2Preface: firstPlaintext));
            if (maxReadSize == 1 ? bytewiseGoAway : goAway)
                CollectionAssert.AreEqual(Convert.FromHexString("000008070000000000000000000000000d"), stream.Written);
            else
                StringAssert.StartsWith(Encoding.ASCII.GetString(stream.Written), "HTTP/1.1 505 ");
            Assert.IsFalse(stream.ReadBlocked.Task.IsCompleted, "A complete first line needs no further preface bytes.");
        }
    }

    [TestMethod]
    [DataRow(3, true)]
    [DataRow(8, true)]
    [DataRow(17, true)]
    [DataRow(18, false)]
    public async Task A_blank_line_before_a_partial_preface_is_consumed_before_the_next_read(int maxReadSize,
        bool goAway)
    {
        await using var stream = new TestDuplexStream("\r\nPRI * HTTP/2.0\r\n"u8.ToArray(), blockAtEnd: true,
            maxReadSize: maxReadSize);
        Assert.IsNull(await WebFront.ReadRequest(stream.Http, TestCt, allowHttp2Preface: true));
        if (goAway)
            CollectionAssert.AreEqual(Convert.FromHexString("000008070000000000000000000000000d"), stream.Written);
        else
            StringAssert.StartsWith(Encoding.ASCII.GetString(stream.Written), "HTTP/1.1 505 ");
        Assert.IsFalse(stream.ReadBlocked.Task.IsCompleted);
    }

    [TestMethod]
    public async Task A_preface_on_a_later_plaintext_request_gets_505()
    {
        await using var stream = new TestDuplexStream(Encoding.ASCII.GetBytes(NextHead + "PRI * HTTP/2.0\r\n"), blockAtEnd: true);
        var request = await WebFront.ReadRequest(stream.Http, TestCt, allowHttp2Preface: true);
        Assert.IsNotNull(request);
        Assert.AreEqual(WebFrontNext.NextRequest, await WebFront.Unauthorized(stream.Http, request, TestCt));
        Assert.IsNull(await WebFront.ReadRequest(stream.Http, TestCt, allowHttp2Preface: false));
        StringAssert.Contains(Encoding.ASCII.GetString(stream.Written), "HTTP/1.1 505 ");
    }
}
