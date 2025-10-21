using VpnHood.Core.Proxies.EndPointManagement.Abstractions;

namespace VpnHood.Test.Tests;

[TestClass]
public class ProxyEndPointParserTests
{
    [TestMethod]
    public void HostParser()
    {
        // basic SOCKS5 URL
        var url = ProxyEndPointParser.TryParseHostToUrl("socks5://user:pass@proxy.example.com:1080", null);
        Assert.IsNotNull(url);
        Assert.AreEqual("socks5", url.Scheme);
        Assert.AreEqual("proxy.example.com", url.Host);
        Assert.AreEqual(1080, url.Port);
        Assert.AreEqual("user:pass", url.UserInfo);

        // HTTP without port -> default 8080
        url = ProxyEndPointParser.TryParseHostToUrl("http://proxy.example.com", null);
        Assert.IsNotNull(url);
        Assert.AreEqual(8080, url.Port); // Default HTTP proxy port

        // HTTPS explicit port
        url = ProxyEndPointParser.TryParseHostToUrl("https://admin:secret@secure-proxy.com:8443", null);
        Assert.IsNotNull(url);
        Assert.AreEqual(8443, url.Port);
        Assert.AreEqual("admin", url.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped).Split(':')[0]);

        // SOCKS4
        url = ProxyEndPointParser.TryParseHostToUrl("socks4://proxy.test:9999", null);
        Assert.IsNotNull(url);
        Assert.AreEqual("socks4", url.Scheme);
        Assert.AreEqual(9999, url.Port);

        // host:port + default protocol (Socks5)
        var d1 = new ProxyEndPointDefaults { Protocol = ProxyProtocol.Socks5 };
        url = ProxyEndPointParser.TryParseHostToUrl("proxy.example.com:1080", d1);
        Assert.IsNotNull(url);
        Assert.AreEqual("socks5", url.Scheme);
        Assert.AreEqual(1080, url.Port);

        // scheme no port + custom default port (should stay protocol default 1080 for socks5)
        var d2 = new ProxyEndPointDefaults { Port = 9090 };
        url = ProxyEndPointParser.TryParseHostToUrl("socks5://proxy.example.com", d2);
        Assert.IsNotNull(url);
        Assert.AreEqual(9090, url.Port); // Should get given port

        // defaults username/password + protocol
        var d3 = new ProxyEndPointDefaults { Protocol = ProxyProtocol.Http, Username = "default-user", Password = "default-pass" };
        url = ProxyEndPointParser.TryParseHostToUrl("proxy.example.com:1080", d3);
        Assert.IsNotNull(url);
        Assert.AreEqual("http", url.Scheme);
        Assert.AreEqual("default-user", url.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped).Split(':')[0]);

        // IPv6
        url = ProxyEndPointParser.TryParseHostToUrl("socks5://[2001:db8::1]:1080", null);
        Assert.IsNotNull(url);
        Assert.AreEqual("[2001:db8::1]", url.Host);
        Assert.AreEqual(1080, url.Port);

        // enabled = false default
        var d4 = new ProxyEndPointDefaults { IsEnabled = false };
        url = ProxyEndPointParser.TryParseHostToUrl("http://proxy.example.com", d4);
        Assert.IsNotNull(url);
        Assert.Contains("enabled=false", url.Query);

        // enabled already present
        url = ProxyEndPointParser.TryParseHostToUrl("http://proxy.example.com?enabled=true", d4);
        Assert.IsNotNull(url);
        Assert.Contains("enabled=true", url.Query);
        Assert.DoesNotContain("enabled=false", url.Query);

        // case insensitive scheme
        url = ProxyEndPointParser.TryParseHostToUrl("SOCKS5://PROXY.EXAMPLE.COM:1080", null);
        Assert.IsNotNull(url);
        Assert.AreEqual("socks5", url.Scheme); // UriBuilder normalizes schemes to lowercase
        Assert.AreEqual("proxy.example.com", url.Host);

        // socks alias
        url = ProxyEndPointParser.TryParseHostToUrl("socks://proxy.example.com", null);
        Assert.IsNotNull(url);
        Assert.AreEqual(1080, url.Port); // Should get SOCKS5 default port
        Assert.Contains("socks", url.Scheme);

        url = ProxyEndPointParser.TryParseHostToUrl("socks5h://proxy.example.com", null);
        Assert.IsNotNull(url);
        Assert.AreEqual(1080, url.Port);

        // encoded user info
        url = ProxyEndPointParser.TryParseHostToUrl("http://user%40domain:p%40ss@proxy.example.com", null);
        Assert.IsNotNull(url);
        var ui = url.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped);
        Assert.Contains("user@domain", ui);

        // invalid inputs
        Assert.IsNull(ProxyEndPointParser.TryParseHostToUrl("", null));
        Assert.IsNull(ProxyEndPointParser.TryParseHostToUrl("   ", null));
        Assert.IsNull(ProxyEndPointParser.TryParseHostToUrl("invalid-url", null));
        Assert.IsNull(ProxyEndPointParser.TryParseHostToUrl("socks5://", null));

        // invalid ports (TryCreate will fail so parser returns null)
        Assert.IsNull(ProxyEndPointParser.TryParseHostToUrl("socks5://proxy.example.com:invalid", null));
        Assert.IsNull(ProxyEndPointParser.TryParseHostToUrl("socks5://proxy.example.com:-1", null));
        Assert.IsNull(ProxyEndPointParser.TryParseHostToUrl("socks5://proxy.example.com:999999", null));

        // mixed defaults: provided credentials override defaults
        var d5 = new ProxyEndPointDefaults { Protocol = ProxyProtocol.Https, Port = 443, Username = "fallback-user", Password = "fallback-pass" };
        url = ProxyEndPointParser.TryParseHostToUrl("user:pass@proxy.example.com:1080", d5);
        Assert.IsNotNull(url);
        Assert.AreEqual("https", url.Scheme);
        Assert.AreEqual(1080, url.Port);
        Assert.AreEqual("user", url.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped).Split(':')[0]);

        // default port override (Protocol default over custom default port when port unspecified)
        var d6 = new ProxyEndPointDefaults { Protocol = ProxyProtocol.Http, Port = 9090 };
        url = ProxyEndPointParser.TryParseHostToUrl("proxy.example.com", d6);
        Assert.IsNotNull(url);
        Assert.AreEqual(9090, url.Port); // Should use protocol 9090

        // only default username
        var d7 = new ProxyEndPointDefaults { Protocol = ProxyProtocol.Socks5, Username = "only-user" };
        url = ProxyEndPointParser.TryParseHostToUrl("proxy.example.com", d7);
        Assert.IsNotNull(url);
        Assert.AreEqual("only-user", url.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped));

        // complex query + enabled default
        var d8 = new ProxyEndPointDefaults { IsEnabled = false };
        url = ProxyEndPointParser.TryParseHostToUrl("http://proxy.example.com?param1=value1&param2=value2", d8);
        Assert.IsNotNull(url);
        Assert.Contains("param1=value1", url.Query);
        Assert.Contains("param2=value2", url.Query);
        Assert.Contains("enabled=false", url.Query);
    }

    [TestMethod]
    public void Extractor()
    {
        Assert.AreEqual(
            new Uri("http://143.198.147.156:8888/"),
            ProxyEndPointParser.ExtractFromText("143.198.147.156 8888 US United States anonymous no 13 secs"));

        Assert.AreEqual(
            new Uri("http://139.59.1.14:80/"),
            ProxyEndPointParser.ExtractFromText("139.59.1.14 80 IN India anonymous no no 13 secs ago"));

        Assert.AreEqual(
            new Uri("https://139.59.1.14:443/"),
            ProxyEndPointParser.ExtractFromText("139.59.1.14 443 IN India anonymous no no 13 secs ago"));

        Assert.AreEqual(
            new Uri("socks5://38.54.71.67:1080/"),
            ProxyEndPointParser.ExtractFromText("this is some proxy 38.54.71.67 1080 username:foo password:pass"));

        Assert.AreEqual(
            new Uri("socks5://38.54.71.67:1080/"),
            ProxyEndPointParser.ExtractFromText("this is some proxy 38.54.71.67 1080"));

        Assert.AreEqual(
            new Uri("socks5://38.54.71.67:1080/"),
            ProxyEndPointParser.ExtractFromText("this is some proxy socks5h://38.54.71.67:1080"),
            "socks5h must be treated as socks5");

        Assert.AreEqual(
            new Uri("socks5://user:pass@38.54.71.67:1080/"),
            ProxyEndPointParser.ExtractFromText("this is some proxy socks5h://user:pass@38.54.71.67:1080"));

        Assert.AreEqual(
            new Uri("http://38.54.71.67:1080/"),
            ProxyEndPointParser.ExtractFromText("{ \"http://user:pass@38.54.71.67:1080\", }"));

        Assert.AreEqual(
            new Uri("socks5://user:pass@mydomain.com:1080/"),
            ProxyEndPointParser.ExtractFromText("something{ \"socks5://user:pass@mydomain.com:1080\", }"));


        // ReSharper disable once StringLiteralTypo
        Assert.AreEqual(
            new Uri("http://[aabb::ccbb]:1080/"),
            ProxyEndPointParser.ExtractFromText("{ \"http://[aabb::ccbb]:1080\", }"), "ip6");


        Assert.IsNull(ProxyEndPointParser.ExtractFromText("71.67:1080"), "bad ip");
        Assert.IsNull(ProxyEndPointParser.ExtractFromText("hello 30 1080 "), "bad ip");
        Assert.IsNull(ProxyEndPointParser.ExtractFromText("1 1 1 5"), "bad ip");
        Assert.IsNull(ProxyEndPointParser.ExtractFromText("1.1.1.1"), "no port");
    }
}