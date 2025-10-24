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

        var merged = ProxyEndPointUpdater.Merge(existing, newEndPoints, maxItemCount: 4, maxPenalty: 5);

        // Order should be: proxy2, proxy1, newproxy1, newproxy2
        // proxy2 has penalty -2 which is <= 0, so it comes AFTER new endpoints
        Assert.HasCount(4, merged);
        
        // First should be previous used with good penalty (proxy3 with penalty 0)
        Assert.AreEqual("proxy2.com", merged[0].Host);
        // Then proxy1 with penalty 5
        Assert.AreEqual("proxy1.com", merged[1].Host);
        // Then new endpoints
        Assert.AreEqual("newproxy1.com", merged[2].Host);
        Assert.AreEqual("newproxy2.com", merged[3].Host);
        
        // proxy2 should not be included (penalty 10 is > maxPenalty)
        Assert.IsFalse(merged.Any(p => p.Host == "proxy3.com"));
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

        // Limit to 4 items - should get: 3 previous used (all have penalty > 0) + 1 new endpoint
        var merged = ProxyEndPointUpdater.Merge(existing, newEndPoints, maxItemCount: 4, maxPenalty: 0);

        Assert.HasCount(4, merged);
        // Order: proxy3 (penalty 8), proxy1 (penalty 5), proxy2 (penalty 3), newproxy1
        Assert.IsTrue(merged.Any(p => p.Host == "proxy3.com"));
        Assert.IsTrue(merged.Any(p => p.Host == "proxy1.com"));
        Assert.IsTrue(merged.Any(p => p.Host == "proxy2.com"));
        Assert.IsTrue(merged.Any(p => p.Host == "newproxy1.com"));
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

        var merged = ProxyEndPointUpdater.Merge(existing, newEndPoints, maxItemCount: 10, maxPenalty: 0);

        // Order: proxy1 (from existing, penalty 5), proxy2 (from existing, penalty 3), then new endpoints
        // But proxy1 is duplicate, so: proxy1, proxy2, newproxy
        Assert.HasCount(3, merged);
        Assert.AreEqual(1, merged.Count(p => p.Host == "proxy1.com"), "Should have exactly one proxy1.com");
        Assert.IsTrue(merged.Any(p => p.Host == "newproxy.com"));
        Assert.IsTrue(merged.Any(p => p.Host == "proxy2.com"));
        
        // proxy1 should appear first (from existing with penalty 5)
        Assert.AreEqual("proxy1.com", merged[0].Host);
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
        
        // Higher penalty comes first
        Assert.AreEqual("proxy2.com", merged[0].Host);
        Assert.AreEqual("proxy1.com", merged[1].Host);
    }

    [TestMethod]
    public void ProxyEndPointUpdater_OrderingTest()
    {
        // Test the exact ordering: 1) used good, 2) new, 3) unused, 4) used bad
        var existing = new[]
        {
            CreateProxyEndPointInfo("used-good.com", 1080, penalty: 5, lastUsedTime: DateTime.UtcNow),
            CreateProxyEndPointInfo("used-bad.com", 1080, penalty: -5, lastUsedTime: DateTime.UtcNow),
            CreateProxyEndPointInfo("unused.com", 1080, penalty: 10, lastUsedTime: null),
        };

        var newEndPoints = new[]
        {
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "new.com", Port = 1080 },
        };

        var merged = ProxyEndPointUpdater.Merge(existing, newEndPoints, maxItemCount: 10, maxPenalty: 0);

        Assert.HasCount(4, merged);
        
        // Order should be: used-good (penalty 5 > 0), new, unused, used-bad (penalty -5 <= 0)
        Assert.AreEqual("used-good.com", merged[0].Host, "First should be used with good penalty");
        Assert.AreEqual("new.com", merged[1].Host, "Second should be new endpoint");
        Assert.AreEqual("unused.com", merged[2].Host, "Third should be unused endpoint");
        Assert.AreEqual("used-bad.com", merged[3].Host, "Fourth should be used with bad penalty");
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
        
        // Order: used1 (10), used4 (8), used2 (5), new, used3 (-2)
        Assert.AreEqual("used1.com", merged[0].Host, "Highest penalty first");
        Assert.AreEqual("used4.com", merged[1].Host, "Second highest penalty");
        Assert.AreEqual("used2.com", merged[2].Host, "Third penalty (still > maxPenalty)");
        Assert.AreEqual("new.com", merged[3].Host, "New endpoint after good used ones");
        Assert.AreEqual("used3.com", merged[4].Host, "Bad penalty last");
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
                LastUsedTime = lastUsedTime
            }
        };
    }
}