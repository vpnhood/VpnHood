using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.Test.Tests;

// An access key is a link that carries data, "vh:" and the base64, which iOS's Avalonia parses with .NET's Uri; the
// vh:// keys given out before are read all the same.
[TestClass]
public class AccessKeyTest : TestBase
{
    [TestMethod]
    public void A_key_is_vh_and_its_base64_which_any_uri_parser_takes()
    {
        var accessKey = Token.FromAccessKey(ClientOptions.SampleAccessKey).ToAccessKey();

        StringAssert.StartsWith(accessKey, "vh:eyJ");
        Assert.IsTrue(Uri.TryCreate(accessKey, UriKind.Absolute, out var uri), "a key is a link");
        Assert.AreEqual("vh", uri.Scheme);
        Assert.AreEqual(accessKey, uri.OriginalString);
    }

    [TestMethod]
    public void A_key_given_out_before_as_vh_slash_slash_reads_the_same()
    {
        var token = Token.FromAccessKey(ClientOptions.SampleAccessKey);
        var oldKey = "vh://" + token.ToAccessKey()["vh:".Length..];

        Assert.AreEqual(token.ToAccessKey(), Token.FromAccessKey(oldKey).ToAccessKey());
        Assert.IsFalse(Uri.TryCreate(oldKey, UriKind.Absolute, out _), "as a host, the key is too long for Uri");
    }

    // the form keys may switch to, whose links the apps already open
    [TestMethod]
    public void A_vhkey_key_reads_the_same_and_is_a_link()
    {
        var token = Token.FromAccessKey(ClientOptions.SampleAccessKey);
        var vhkeyKey = "vhkey:" + token.ToAccessKey()["vh:".Length..];

        Assert.AreEqual(token.ToAccessKey(), Token.FromAccessKey(vhkeyKey).ToAccessKey());
        Assert.IsTrue(Uri.TryCreate(vhkeyKey, UriKind.Absolute, out var uri), "a key is a link");
        Assert.AreEqual("vhkey", uri.Scheme);
    }
}
