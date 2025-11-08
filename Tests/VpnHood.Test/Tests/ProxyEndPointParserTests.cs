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

    [TestMethod]
    public void ProxyEndPointUpdater_BasicMerge()
    {
        // Create some existing endpoints with different penalties
        var existing = new[]
        {
            CreateProxyEndPointInfo("proxy1.com", 1080, penalty: 5, lastUsedTime: DateTime.UtcNow.AddMinutes(-5)),
            CreateProxyEndPointInfo("proxy2.com", 1080, penalty: 0, lastUsedTime: DateTime.UtcNow.AddMinutes(-10)),
            CreateProxyEndPointInfo("proxy3.com", 1080, penalty: 10, lastUsedTime: DateTime.UtcNow.AddMinutes(-15)),
        };

        // Create new endpoints
        var newEndPoints = new[]
        {
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "newproxy1.com", Port = 1080 },
            new ProxyEndPoint { Protocol = ProxyProtocol.Http, Host = "newproxy2.com", Port = 8080 },
        };

        var merged = ProxyEndPointUpdater.Merge(existing, newEndPoints, maxItemCount: 1000, maxPenalty: 5);

        // Order should be: proxy2, proxy1, newproxy1, newproxy2
        Assert.HasCount(5, merged);
        Assert.AreEqual("proxy2.com", merged[0].Host);
        Assert.AreEqual("proxy1.com", merged[1].Host);
        Assert.AreEqual("newproxy1.com", merged[2].Host);
        Assert.AreEqual("newproxy2.com", merged[3].Host);
        Assert.AreEqual("proxy3.com", merged[4].Host);
    }

    [TestMethod]
    public void ProxyEndPointUpdater_MaxItemCountLimit()
    {
        var existing = new[]
        {
            CreateProxyEndPointInfo("proxy1.com", 1080, penalty: 5, lastUsedTime: DateTime.UtcNow),
            CreateProxyEndPointInfo("proxy2.com", 1080, penalty: 3, lastUsedTime: DateTime.UtcNow),
            CreateProxyEndPointInfo("proxy3.com", 1080, penalty: 8, lastUsedTime: DateTime.UtcNow),
        };

        var newEndPoints = new[]
        {
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "newproxy1.com", Port = 1080 },
            new ProxyEndPoint { Protocol = ProxyProtocol.Http, Host = "newproxy2.com", Port = 8080 },
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "newproxy3.com", Port = 1081 },
        };

        // With maxPenalty 0, no used-good items exist; ordering is: new endpoints first, then used-bad ascending by penalty.
        // Limited to 4 items -> expect: newproxy1, newproxy2, newproxy3, proxy2.com (penalty 3)
        var merged = ProxyEndPointUpdater.Merge(existing, newEndPoints, maxItemCount: 4, maxPenalty: 0);

        Assert.HasCount(4, merged);
        Assert.AreEqual("newproxy1.com", merged[0].Host);
        Assert.AreEqual("newproxy2.com", merged[1].Host);
        Assert.AreEqual("newproxy3.com", merged[2].Host);
        Assert.AreEqual("proxy2.com", merged[3].Host);
    }

    [TestMethod]
    public void ProxyEndPointUpdater_DuplicateHandling()
    {
        var existing = new[]
        {
            CreateProxyEndPointInfo("proxy1.com", 1080, penalty: 5, lastUsedTime: DateTime.UtcNow),
            CreateProxyEndPointInfo("proxy2.com", 1080, penalty: 3, lastUsedTime: DateTime.UtcNow),
        };

        // New list contains one duplicate
        var newEndPoints = new[]
        {
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "proxy1.com", Port = 1080 },
            new ProxyEndPoint { Protocol = ProxyProtocol.Http, Host = "newproxy.com", Port = 8080 },
        };

        var merged = ProxyEndPointUpdater.Merge(existing, newEndPoints, maxItemCount: 3, maxPenalty: 0);

        // Order with maxPenalty 0: new endpoints first (deduped), then used-bad ascending by penalty
        // Expect: proxy1.com (from new), newproxy.com, proxy2.com
        Assert.HasCount(3, merged);
        Assert.AreEqual(1, merged.Count(p => p.Host == "proxy1.com"), "Should have exactly one proxy1.com");
        Assert.AreEqual("proxy1.com", merged[0].Host);
        Assert.AreEqual("newproxy.com", merged[1].Host);
        Assert.AreEqual("proxy2.com", merged[2].Host);
    }

    [TestMethod]
    public void ProxyEndPointUpdater_InvalidMaxItemCount()
    {
        var existing = new[]
        {
            CreateProxyEndPointInfo("proxy1.com", 1080, penalty: 5),
        };
        var newEndPoints = new[]
        {
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "newproxy.com", Port = 1080 },
        };

        Assert.Throws<ArgumentException>(() =>
            ProxyEndPointUpdater.Merge(existing, newEndPoints, maxItemCount: 0, maxPenalty: 0));

        Assert.Throws<ArgumentException>(() =>
            ProxyEndPointUpdater.Merge(existing, newEndPoints, maxItemCount: -1, maxPenalty: 0));
    }

    [TestMethod]
    public void ProxyEndPointUpdater_AllNewEndPoints()
    {
        var existing = Array.Empty<ProxyEndPointInfo>();
        var newEndPoints = new[]
        {
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "newproxy1.com", Port = 1080 },
            new ProxyEndPoint { Protocol = ProxyProtocol.Http, Host = "newproxy2.com", Port = 8080 },
        };

        var merged = ProxyEndPointUpdater.Merge(existing, newEndPoints, maxItemCount: 10, maxPenalty: 0);

        Assert.HasCount(2, merged);
        Assert.IsTrue(merged.Any(p => p.Host == "newproxy1.com"));
        Assert.IsTrue(merged.Any(p => p.Host == "newproxy2.com"));
    }

    [TestMethod]
    public void ProxyEndPointUpdater_AllExistingWithHighPenalty()
    {
        var existing = new[]
        {
            CreateProxyEndPointInfo("proxy1.com", 1080, penalty: 5, lastUsedTime: DateTime.UtcNow),
            CreateProxyEndPointInfo("proxy2.com", 1080, penalty: 8, lastUsedTime: DateTime.UtcNow),
        };
        var newEndPoints = Array.Empty<ProxyEndPoint>();

        var merged = ProxyEndPointUpdater.Merge(existing, newEndPoints, maxItemCount: 10, maxPenalty: 0);

        Assert.HasCount(2, merged);
        Assert.IsTrue(merged.Any(p => p.Host == "proxy1.com"));
        Assert.IsTrue(merged.Any(p => p.Host == "proxy2.com"));
        
        // Lower penalty is better, and used-bad are sorted ascending by penalty
        Assert.AreEqual("proxy1.com", merged[0].Host);
        Assert.AreEqual("proxy2.com", merged[1].Host);
    }

    [TestMethod]
    public void ProxyEndPointUpdater_OrderingTest()
    {
        // Test the exact ordering: 1) used good (<= maxPenalty), 2) new, 3) unused, 4) used bad (> maxPenalty)
        var existing = new[]
        {
            CreateProxyEndPointInfo("used-good.com", 1080, penalty: 5, lastUsedTime: DateTime.UtcNow), // actually bad for maxPenalty=0
            CreateProxyEndPointInfo("used-bad.com", 1080, penalty: -5, lastUsedTime: DateTime.UtcNow), // actually good for maxPenalty=0
            CreateProxyEndPointInfo("unused.com", 1080, penalty: 10, lastUsedTime: null),
        };

        var newEndPoints = new[]
        {
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "new.com", Port = 1080 },
        };

        var merged = ProxyEndPointUpdater.Merge(existing, newEndPoints, maxItemCount: 10, maxPenalty: 0);

        Assert.HasCount(4, merged);
        
        // With maxPenalty=0, order should be: used-bad(-5) as good, then new, then unused, then used-good(5) as bad
        Assert.AreEqual("used-bad.com", merged[0].Host, "First should be used with good (lowest) penalty");
        Assert.AreEqual("new.com", merged[1].Host, "Second should be new endpoint");
        Assert.AreEqual("unused.com", merged[2].Host, "Third should be unused endpoint");
        Assert.AreEqual("used-good.com", merged[3].Host, "Fourth should be used with bad (higher) penalty");
    }

    [TestMethod]
    public void ProxyEndPointUpdater_MultipleUsedWithDifferentPenalties()
    {
        var existing = new[]
        {
            CreateProxyEndPointInfo("used1.com", 1080, penalty: 10, lastUsedTime: DateTime.UtcNow),
            CreateProxyEndPointInfo("used2.com", 1080, penalty: 5, lastUsedTime: DateTime.UtcNow),
            CreateProxyEndPointInfo("used3.com", 1080, penalty: -2, lastUsedTime: DateTime.UtcNow),
            CreateProxyEndPointInfo("used4.com", 1080, penalty: 8, lastUsedTime: DateTime.UtcNow),
        };

        var newEndPoints = new[]
        {
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "new.com", Port = 1080 },
        };

        var merged = ProxyEndPointUpdater.Merge(existing, newEndPoints, maxItemCount: 10, maxPenalty: 0);

        Assert.HasCount(5, merged);
        
        // Order with lower-penalty-better: used3 (-2), new, then used-bad ascending (5,8,10)
        Assert.AreEqual("used3.com", merged[0].Host, "Lowest penalty first");
        Assert.AreEqual("new.com", merged[1].Host, "New endpoint next");
        Assert.AreEqual("used2.com", merged[2].Host, "Third penalty (still > maxPenalty)");
        Assert.AreEqual("used4.com", merged[3].Host, "Then next bad");
        Assert.AreEqual("used1.com", merged[4].Host, "Highest penalty last");
    }

    // Helper method to create ProxyEndPointInfo with custom penalty and LastUsedTime
    private static ProxyEndPointInfo CreateProxyEndPointInfo(string host, int port, int penalty, DateTime? lastUsedTime = null)
    {
        return new ProxyEndPointInfo
        {
            EndPoint = new ProxyEndPoint
            {
                Protocol = ProxyProtocol.Socks5,
                Host = host,
                Port = port
            },
            Status = new ProxyEndPointStatus
            {
                Penalty = penalty,
                LastSucceeded = lastUsedTime,
                SucceededCount = lastUsedTime != null ? 1 : 0 // make sure has used
            }
        };
    }
}