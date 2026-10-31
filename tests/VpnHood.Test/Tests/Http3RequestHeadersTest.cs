using System.Text;
using VpnHood.Net.Http3;

namespace VpnHood.Test.Tests;

[TestClass]
public class Http3RequestHeadersTest
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    [DataRow("80")]
    [DataRow("c080")]
    [DataRow("e08080")]
    [DataRow("eda080")]
    [DataRow("f4908080")]
    [DataRow("c2")]
    public async Task Invalid_utf8_values_fail_as_http_messages_not_compression_errors(string hex)
    {
        var exception = await Assert.ThrowsExactlyAsync<Http3ProtocolException>(() =>
            ReadHeaders(Convert.FromHexString(hex), TestContext.CancellationToken));
        Assert.AreEqual(Http3Protocol.MessageError, exception.ErrorCode);
        Assert.IsFalse(exception.ConnectionError);
    }

    [TestMethod]
    [DataRow("c3a9")]
    [DataRow("e29883")]
    [DataRow("f09f9880")]
    public async Task Valid_multibyte_values_are_accepted(string hex)
    {
        var value = Convert.FromHexString(hex);
        var headers = await ReadHeaders(value, TestContext.CancellationToken);
        Assert.AreEqual(Encoding.UTF8.GetString(value), headers.Fields.Single().Value);
    }

    [TestMethod]
    public async Task Larger_values_use_the_same_strict_decoding_rules()
    {
        var prefix = Encoding.ASCII.GetBytes(new string('a', 300));
        var headers = await ReadHeaders([.. prefix, 0xc3, 0xa9], TestContext.CancellationToken);
        Assert.AreEqual(new string('a', 300) + "é", headers.Fields.Single().Value);
        var exception = await Assert.ThrowsExactlyAsync<Http3ProtocolException>(() =>
            ReadHeaders([.. prefix, 0x80], TestContext.CancellationToken));
        Assert.AreEqual(Http3Protocol.MessageError, exception.ErrorCode);
    }

    private static async Task<Http3RequestHeaders> ReadHeaders(byte[] value, CancellationToken cancellationToken)
    {
        List<byte> bytes = [0, 0, 0x26, .. "x-test"u8];
        var length = value.Length;
        bytes.Add((byte)Math.Min(length, 127));
        if (length >= 127) {
            length -= 127;
            while (length >= 128) {
                bytes.Add((byte)(0x80 | (length & 0x7f)));
                length >>= 7;
            }
            bytes.Add((byte)length);
        }
        bytes.AddRange(value);
        using var stream = new MemoryStream(bytes.ToArray());
        await using var reader = new Http3StreamReader(stream);
        var headers = new Http3RequestHeaders(trailers: false, new Http3ServerOptions().MaxRequestLineBytes);
        var section = await Http3Qpack.ReadSection(reader, bytes.Count, headers.Append,
            new Http3ServerOptions(), cancellationToken);
        Assert.IsTrue(section.Completed);
        return headers;
    }
}
