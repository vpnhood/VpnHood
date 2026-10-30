using System.Text;
using VpnHood.Core.Server.Listeners.Http3;

namespace VpnHood.Test.Tests;

[TestClass]
public class Http3RequestHeadersTest
{
    [TestMethod]
    [DataRow("80")]
    [DataRow("c080")]
    [DataRow("e08080")]
    [DataRow("eda080")]
    [DataRow("f4908080")]
    [DataRow("c2")]
    public void Invalid_utf8_values_fail_as_http_messages_not_compression_errors(string hex)
    {
        var headers = new Http3RequestHeaders(trailers: false);
        var value = Encoding.Latin1.GetString(Convert.FromHexString(hex));
        var exception = Assert.ThrowsExactly<Http3ProtocolException>(() =>
            headers.Append(new KeyValuePair<string, string>("x-test", value)));
        Assert.AreEqual(Http3Protocol.MessageError, exception.ErrorCode);
        Assert.IsFalse(exception.ConnectionError);
    }

    [TestMethod]
    [DataRow("c3a9")]
    [DataRow("e29883")]
    [DataRow("f09f9880")]
    public void Valid_multibyte_values_are_accepted(string hex)
    {
        var headers = new Http3RequestHeaders(trailers: false);
        var value = Encoding.Latin1.GetString(Convert.FromHexString(hex));
        headers.Append(new KeyValuePair<string, string>("x-test", value));
    }

    [TestMethod]
    public void Larger_values_use_the_same_strict_decoding_rules()
    {
        var headers = new Http3RequestHeaders(trailers: false);
        var prefix = new string('a', 300);
        headers.Append(new KeyValuePair<string, string>("x-test", prefix + "\u00c3\u00a9"));
        var exception = Assert.ThrowsExactly<Http3ProtocolException>(() =>
            headers.Append(new KeyValuePair<string, string>("x-test", prefix + "\u0080")));
        Assert.AreEqual(Http3Protocol.MessageError, exception.ErrorCode);
    }
}
