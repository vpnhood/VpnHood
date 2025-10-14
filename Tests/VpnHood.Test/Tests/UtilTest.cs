using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions.ProxyNodes;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Test.Tests;

[TestClass]
public class UtilTest : TestBase
{
    private class TestEventReporter(string message, TimeSpan period) 
        : EventReporter(message, period: period)
    {
        public int ReportedCount { get; private set; }

        protected override void Report()
        {
            base.Report();
            ReportedCount++;
        }
    }

    [TestMethod]
    public async Task EventReportCounter()
    {
        VhLogger.MinLogLevel = LogLevel.Information;
        using var reportCounter = new TestEventReporter("UnitTest", period: TimeSpan.FromMilliseconds(1000));

        Assert.AreEqual(0, reportCounter.ReportedCount);
        reportCounter.Raise();
        Assert.AreEqual(1, reportCounter.TotalEventCount);
        await VhTestUtil.AssertEqualsWait(1, ()=>reportCounter.ReportedCount);

        reportCounter.Raise();
        reportCounter.Raise();
        reportCounter.Raise();
        Assert.AreEqual(4, reportCounter.TotalEventCount);
        Assert.AreEqual(1, reportCounter.ReportedCount);

        // wait for the next report
        Assert.AreEqual(4, reportCounter.TotalEventCount);
        await VhTestUtil.AssertEqualsWait(2, ()=>reportCounter.ReportedCount);

        reportCounter.Raise(); // immediate
        Assert.AreEqual(5, reportCounter.TotalEventCount);
        Assert.AreEqual(2, reportCounter.ReportedCount);
        await VhTestUtil.AssertEqualsWait(3, ()=>reportCounter.ReportedCount);
    }

    [TestMethod]
    public void VhUrlParser_validate()
    {
        // basic SOCKS5 URL
        Assert.IsTrue(VhUrlParser.TryParse("socks5://user:pass@proxy.example.com:1080", null, out var url));
        Assert.IsNotNull(url);
        Assert.AreEqual("socks5", url.Scheme);
        Assert.AreEqual("proxy.example.com", url.Host);
        Assert.AreEqual(1080, url.Port);
        Assert.AreEqual("user:pass", url.UserInfo);

        // HTTP without port -> default 8080
        Assert.IsTrue(VhUrlParser.TryParse("http://proxy.example.com", null, out url));
        Assert.IsNotNull(url);
        Assert.AreEqual(8080, url.Port); // Default HTTP proxy port

        // HTTPS explicit port
        Assert.IsTrue(VhUrlParser.TryParse("https://admin:secret@secure-proxy.com:8443", null, out url));
        Assert.IsNotNull(url);
        Assert.AreEqual(8443, url.Port);
        Assert.AreEqual("admin", url.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped).Split(':')[0]);

        // SOCKS4
        Assert.IsTrue(VhUrlParser.TryParse("socks4://proxy.test:9999", null, out url));
        Assert.IsNotNull(url);
        Assert.AreEqual("socks4", url.Scheme);
        Assert.AreEqual(9999, url.Port);

        // host:port + default protocol (Socks5)
        var d1 = new ProxyNodeDefaults { Protocol = ProxyProtocol.Socks5 };
        Assert.IsTrue(VhUrlParser.TryParse("proxy.example.com:1080", d1, out url));
        Assert.IsNotNull(url);
        Assert.AreEqual("socks5", url.Scheme);
        Assert.AreEqual(1080, url.Port);

        // scheme no port + custom default port (should stay protocol default 1080 for socks5)
        var d2 = new ProxyNodeDefaults { Port = 9090 };
        Assert.IsTrue(VhUrlParser.TryParse("socks5://proxy.example.com", d2, out url));
        Assert.IsNotNull(url);
        Assert.AreEqual(9090, url.Port); // Should get given port

        // defaults username/password + protocol
        var d3 = new ProxyNodeDefaults { Protocol = ProxyProtocol.Http, Username = "default-user", Password = "default-pass" };
        Assert.IsTrue(VhUrlParser.TryParse("proxy.example.com:1080", d3, out url));
        Assert.IsNotNull(url);
        Assert.AreEqual("http", url.Scheme);
        Assert.AreEqual("default-user", url.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped).Split(':')[0]);

        // IPv6
        Assert.IsTrue(VhUrlParser.TryParse("socks5://[2001:db8::1]:1080", null, out url));
        Assert.IsNotNull(url);
        Assert.AreEqual("[2001:db8::1]", url.Host);
        Assert.AreEqual(1080, url.Port);

        // enabled = false default
        var d4 = new ProxyNodeDefaults { IsEnabled = false };
        Assert.IsTrue(VhUrlParser.TryParse("http://proxy.example.com", d4, out url));
        Assert.IsNotNull(url);
        Assert.Contains("enabled=false", url.Query);

        // enabled already present
        Assert.IsTrue(VhUrlParser.TryParse("http://proxy.example.com?enabled=true", d4, out url));
        Assert.IsNotNull(url);
        Assert.Contains("enabled=true", url.Query);
        Assert.DoesNotContain("enabled=false", url.Query);

        // case insensitive scheme
        Assert.IsTrue(VhUrlParser.TryParse("SOCKS5://PROXY.EXAMPLE.COM:1080", null, out url));
        Assert.IsNotNull(url);
        Assert.AreEqual("socks5", url.Scheme); // UriBuilder normalizes schemes to lowercase
        Assert.AreEqual("proxy.example.com", url.Host);

        // socks alias
        Assert.IsTrue(VhUrlParser.TryParse("socks://proxy.example.com", null, out url));
        Assert.IsNotNull(url);
        Assert.AreEqual(1080, url.Port); // Should get SOCKS5 default port
        Assert.Contains("socks", url.Scheme);

        Assert.IsTrue(VhUrlParser.TryParse("socks5h://proxy.example.com", null, out url));
        Assert.IsNotNull(url);
        Assert.AreEqual(1080, url.Port);

        // encoded user info
        Assert.IsTrue(VhUrlParser.TryParse("http://user%40domain:p%40ss@proxy.example.com", null, out url));
        Assert.IsNotNull(url);
        var ui = url.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped);
        Assert.Contains("user@domain", ui);

        // invalid inputs
        Assert.IsFalse(VhUrlParser.TryParse("", null, out _));
        Assert.IsFalse(VhUrlParser.TryParse("   ", null, out _));
        Assert.IsFalse(VhUrlParser.TryParse("invalid-url", null, out _));
        Assert.IsFalse(VhUrlParser.TryParse("socks5://", null, out _));

        // invalid ports (TryCreate will fail so parser returns false)
        Assert.IsFalse(VhUrlParser.TryParse("socks5://proxy.example.com:invalid", null, out _));
        Assert.IsFalse(VhUrlParser.TryParse("socks5://proxy.example.com:-1", null, out _));
        Assert.IsFalse(VhUrlParser.TryParse("socks5://proxy.example.com:999999", null, out _));

        // mixed defaults: provided credentials override defaults
        var d5 = new ProxyNodeDefaults { Protocol = ProxyProtocol.Https, Port = 443, Username = "fallback-user", Password = "fallback-pass" };
        Assert.IsTrue(VhUrlParser.TryParse("user:pass@proxy.example.com:1080", d5, out url));
        Assert.IsNotNull(url);
        Assert.AreEqual("https", url.Scheme);
        Assert.AreEqual(1080, url.Port);
        Assert.AreEqual("user", url.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped).Split(':')[0]);

        // default port override (Protocol default over custom default port when port unspecified)
        var d6 = new ProxyNodeDefaults { Protocol = ProxyProtocol.Http, Port = 9090 };
        Assert.IsTrue(VhUrlParser.TryParse("proxy.example.com", d6, out url));
        Assert.IsNotNull(url);
        Assert.AreEqual(9090, url.Port); // Should use protocol 9090

        // only default username
        var d7 = new ProxyNodeDefaults { Protocol = ProxyProtocol.Socks5, Username = "only-user" };
        Assert.IsTrue(VhUrlParser.TryParse("proxy.example.com", d7, out url));
        Assert.IsNotNull(url);
        Assert.AreEqual("only-user", url.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped));

        // complex query + enabled default
        var d8 = new ProxyNodeDefaults { IsEnabled = false };
        Assert.IsTrue(VhUrlParser.TryParse("http://proxy.example.com?param1=value1&param2=value2", d8, out url));
        Assert.IsNotNull(url);
        Assert.Contains("param1=value1", url.Query);
        Assert.Contains("param2=value2", url.Query);
        Assert.Contains("enabled=false", url.Query);
    }
}