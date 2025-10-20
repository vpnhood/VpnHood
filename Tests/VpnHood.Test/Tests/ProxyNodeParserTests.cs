using VpnHood.Core.Client.Abstractions.ProxyNodes;

namespace VpnHood.Test.Tests;

[TestClass]
public class ProxyNodeParserTests
{
    [TestMethod]
    public void HostParser()
    {
        // basic SOCKS5 URL
        var url = ProxyNodeParser.TryParseToUrl("socks5://user:pass@proxy.example.com:1080", null);
        Assert.IsNotNull(url);
        Assert.AreEqual("socks5", url.Scheme);
        Assert.AreEqual("proxy.example.com", url.Host);
        Assert.AreEqual(1080, url.Port);
        Assert.AreEqual("user:pass", url.UserInfo);

        // HTTP without port -> default 8080
        url = ProxyNodeParser.TryParseToUrl("http://proxy.example.com", null);
        Assert.IsNotNull(url);
        Assert.AreEqual(8080, url.Port); // Default HTTP proxy port

        // HTTPS explicit port
        url = ProxyNodeParser.TryParseToUrl("https://admin:secret@secure-proxy.com:8443", null);
        Assert.IsNotNull(url);
        Assert.AreEqual(8443, url.Port);
        Assert.AreEqual("admin", url.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped).Split(':')[0]);

        // SOCKS4
        url = ProxyNodeParser.TryParseToUrl("socks4://proxy.test:9999", null);
        Assert.IsNotNull(url);
        Assert.AreEqual("socks4", url.Scheme);
        Assert.AreEqual(9999, url.Port);

        // host:port + default protocol (Socks5)
        var d1 = new ProxyNodeDefaults { Protocol = ProxyProtocol.Socks5 };
        url = ProxyNodeParser.TryParseToUrl("proxy.example.com:1080", d1);
        Assert.IsNotNull(url);
        Assert.AreEqual("socks5", url.Scheme);
        Assert.AreEqual(1080, url.Port);

        // scheme no port + custom default port (should stay protocol default 1080 for socks5)
        var d2 = new ProxyNodeDefaults { Port = 9090 };
        url = ProxyNodeParser.TryParseToUrl("socks5://proxy.example.com", d2);
        Assert.IsNotNull(url);
        Assert.AreEqual(9090, url.Port); // Should get given port

        // defaults username/password + protocol
        var d3 = new ProxyNodeDefaults { Protocol = ProxyProtocol.Http, Username = "default-user", Password = "default-pass" };
        url = ProxyNodeParser.TryParseToUrl("proxy.example.com:1080", d3);
        Assert.IsNotNull(url);
        Assert.AreEqual("http", url.Scheme);
        Assert.AreEqual("default-user", url.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped).Split(':')[0]);

        // IPv6
        url = ProxyNodeParser.TryParseToUrl("socks5://[2001:db8::1]:1080", null);
        Assert.IsNotNull(url);
        Assert.AreEqual("[2001:db8::1]", url.Host);
        Assert.AreEqual(1080, url.Port);

        // enabled = false default
        var d4 = new ProxyNodeDefaults { IsEnabled = false };
        url = ProxyNodeParser.TryParseToUrl("http://proxy.example.com", d4);
        Assert.IsNotNull(url);
        Assert.Contains("enabled=false", url.Query);

        // enabled already present
        url = ProxyNodeParser.TryParseToUrl("http://proxy.example.com?enabled=true", d4);
        Assert.IsNotNull(url);
        Assert.Contains("enabled=true", url.Query);
        Assert.DoesNotContain("enabled=false", url.Query);

        // case insensitive scheme
        url = ProxyNodeParser.TryParseToUrl("SOCKS5://PROXY.EXAMPLE.COM:1080", null);
        Assert.IsNotNull(url);
        Assert.AreEqual("socks5", url.Scheme); // UriBuilder normalizes schemes to lowercase
        Assert.AreEqual("proxy.example.com", url.Host);

        // socks alias
        url = ProxyNodeParser.TryParseToUrl("socks://proxy.example.com", null);
        Assert.IsNotNull(url);
        Assert.AreEqual(1080, url.Port); // Should get SOCKS5 default port
        Assert.Contains("socks", url.Scheme);

        url = ProxyNodeParser.TryParseToUrl("socks5h://proxy.example.com", null);
        Assert.IsNotNull(url);
        Assert.AreEqual(1080, url.Port);

        // encoded user info
        url = ProxyNodeParser.TryParseToUrl("http://user%40domain:p%40ss@proxy.example.com", null);
        Assert.IsNotNull(url);
        var ui = url.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped);
        Assert.Contains("user@domain", ui);

        // invalid inputs
        Assert.IsNull(ProxyNodeParser.TryParseToUrl("", null));
        Assert.IsNull(ProxyNodeParser.TryParseToUrl("   ", null));
        Assert.IsNull(ProxyNodeParser.TryParseToUrl("invalid-url", null));
        Assert.IsNull(ProxyNodeParser.TryParseToUrl("socks5://", null));

        // invalid ports (TryCreate will fail so parser returns null)
        Assert.IsNull(ProxyNodeParser.TryParseToUrl("socks5://proxy.example.com:invalid", null));
        Assert.IsNull(ProxyNodeParser.TryParseToUrl("socks5://proxy.example.com:-1", null));
        Assert.IsNull(ProxyNodeParser.TryParseToUrl("socks5://proxy.example.com:999999", null));

        // mixed defaults: provided credentials override defaults
        var d5 = new ProxyNodeDefaults { Protocol = ProxyProtocol.Https, Port = 443, Username = "fallback-user", Password = "fallback-pass" };
        url = ProxyNodeParser.TryParseToUrl("user:pass@proxy.example.com:1080", d5);
        Assert.IsNotNull(url);
        Assert.AreEqual("https", url.Scheme);
        Assert.AreEqual(1080, url.Port);
        Assert.AreEqual("user", url.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped).Split(':')[0]);

        // default port override (Protocol default over custom default port when port unspecified)
        var d6 = new ProxyNodeDefaults { Protocol = ProxyProtocol.Http, Port = 9090 };
        url = ProxyNodeParser.TryParseToUrl("proxy.example.com", d6);
        Assert.IsNotNull(url);
        Assert.AreEqual(9090, url.Port); // Should use protocol 9090

        // only default username
        var d7 = new ProxyNodeDefaults { Protocol = ProxyProtocol.Socks5, Username = "only-user" };
        url = ProxyNodeParser.TryParseToUrl("proxy.example.com", d7);
        Assert.IsNotNull(url);
        Assert.AreEqual("only-user", url.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped));

        // complex query + enabled default
        var d8 = new ProxyNodeDefaults { IsEnabled = false };
        url = ProxyNodeParser.TryParseToUrl("http://proxy.example.com?param1=value1&param2=value2", d8);
        Assert.IsNotNull(url);
        Assert.Contains("param1=value1", url.Query);
        Assert.Contains("param2=value2", url.Query);
        Assert.Contains("enabled=false", url.Query);
    }

    [TestMethod]
    public void BulkParser()
    {
        // Arrange
        var lines = new[]
        {
            "143.198.147.156\t8888\tUS\tUnited States\tanonymous\tno\tyes\t13 secs ago",
            "38.54.71.67\t80\tNP\tNepal\telite proxy\tno\tno\t13 secs ago",
            "139.59.1.14\t80\tIN\tIndia\tanonymous\tno\tno\t13 secs ago",
            "123.30.154.171\t7777\tVN\tVietnam\tanonymous\tno\tno\t13 secs ago",
            "http://user:pass@203.0.113.5:8080 Some extra columns",
            "socks5://198.51.100.10:1080\twhatever",
            "bad line without host",
            "10.0.0.1:443\tmaybe-https"
        };

        var expectedUris = new[]
        {
            "http://143.198.147.156:8888/",
            "http://38.54.71.67:80/",
            "http://139.59.1.14:80/",
            "http://123.30.154.171:7777/",
            "http://user:pass@203.0.113.5:8080/",
            "socks5://198.51.100.10:1080/",
            "https://10.0.0.1:443/"
        };

        // Act
        var uris = ProxyNodeParser.ParseTextToUrls(string.Join(Environment.NewLine, lines), defaultScheme: "http", preferHttpsWhenPort443: true);

        // Assert
        Assert.HasCount(expectedUris.Length, uris, "Number of parsed URIs mismatch.");
        
        var actual = uris.Select(u => u.ToString()).ToArray();
        CollectionAssert.AreEquivalent(expectedUris, actual, "Parsed URIs do not match expected output.");

        // Optional sanity check: No unexpected empty URIs
        Assert.IsFalse(uris.Any(u => string.IsNullOrWhiteSpace(u.Host)), "Some URIs have empty host.");
    }
}