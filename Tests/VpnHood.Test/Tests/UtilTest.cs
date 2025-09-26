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
        // Test with LogLevel.Information
        VhLogger.MinLogLevel = LogLevel.Information;

        using var reportCounter = new TestEventReporter("UnitTest", period: TimeSpan.FromMilliseconds(1000));

        Assert.AreEqual(0, reportCounter.ReportedCount);

        reportCounter.Raise(); // report
        Assert.AreEqual(1, reportCounter.TotalEventCount);
        await VhTestUtil.AssertEqualsWait(1, ()=>reportCounter.ReportedCount);

        reportCounter.Raise(); // wait
        reportCounter.Raise(); // wait
        reportCounter.Raise(); // wait
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
        // Test basic SOCKS5 URL
        Assert.IsTrue(VhUrlParser.TryParse("socks5://user:pass@proxy.example.com:1080", 
            null, null, null, null, null, out var url1));
        Assert.AreEqual("socks5", url1.Scheme);
        Assert.AreEqual("proxy.example.com", url1.Host);
        Assert.AreEqual(1080, url1.Port);
        Assert.AreEqual("user:pass", url1.UserInfo);

        // Test HTTP URL without port (should use default)
        Assert.IsTrue(VhUrlParser.TryParse("http://proxy.example.com", 
            null, null, null, null, null, out var url2));
        Assert.AreEqual("http", url2.Scheme);
        Assert.AreEqual("proxy.example.com", url2.Host);
        Assert.AreEqual(8080, url2.Port); // Default HTTP proxy port

        // Test HTTPS URL with explicit port
        Assert.IsTrue(VhUrlParser.TryParse("https://admin:secret@secure-proxy.com:8443", 
            null, null, null, null, null, out var url3));
        Assert.AreEqual("https", url3.Scheme);
        Assert.AreEqual("secure-proxy.com", url3.Host);
        Assert.AreEqual(8443, url3.Port);
        Assert.AreEqual("admin", url3.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped).Split(':')[0]);

        // Test SOCKS4 URL
        Assert.IsTrue(VhUrlParser.TryParse("socks4://proxy.test:9999", 
            null, null, null, null, null, out var url4));
        Assert.AreEqual("socks4", url4.Scheme);
        Assert.AreEqual("proxy.test", url4.Host);
        Assert.AreEqual(9999, url4.Port);

        // Test URL without scheme using default protocol
        Assert.IsTrue(VhUrlParser.TryParse("proxy.example.com:1080", 
            ProxyProtocol.Socks5, null, null, null, null, out var url5));
        Assert.AreEqual("socks5", url5.Scheme);
        Assert.AreEqual("proxy.example.com", url5.Host);
        Assert.AreEqual(1080, url5.Port);

        // Test URL without port using default port
        Assert.IsTrue(VhUrlParser.TryParse("socks5://proxy.example.com", 
            null, 9090, null, null, null, out var url6));
        Assert.AreEqual("socks5", url6.Scheme);
        Assert.AreEqual("proxy.example.com", url6.Host);
        Assert.AreEqual(1080, url6.Port); // Should use protocol default, not custom default

        // Test URL with default username and password
        Assert.IsTrue(VhUrlParser.TryParse("proxy.example.com:1080", 
            ProxyProtocol.Http, null, "default-user", "default-pass", null, out var url7));
        Assert.AreEqual("http", url7.Scheme);
        Assert.AreEqual("proxy.example.com", url7.Host);
        Assert.AreEqual(1080, url7.Port);
        Assert.AreEqual("default-user", url7.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped).Split(':')[0]);

        // Test URL with IPv6 address
        Assert.IsTrue(VhUrlParser.TryParse("socks5://[2001:db8::1]:1080", 
            null, null, null, null, null, out var url8));
        Assert.AreEqual("socks5", url8.Scheme);
        Assert.AreEqual("[2001:db8::1]", url8.Host);
        Assert.AreEqual(1080, url8.Port);

        // Test URL with enabled=false default
        Assert.IsTrue(VhUrlParser.TryParse("http://proxy.example.com", 
            null, null, null, null, false, out var url9));
        Assert.IsTrue(url9.Query.Contains("enabled=false"));

        // Test URL already with enabled parameter (should not override)
        Assert.IsTrue(VhUrlParser.TryParse("http://proxy.example.com?enabled=true", 
            null, null, null, null, false, out var url10));
        Assert.IsTrue(url10.Query.Contains("enabled=true"));
        Assert.IsFalse(url10.Query.Contains("enabled=false"));

        // Test case-insensitive schemes
        Assert.IsTrue(VhUrlParser.TryParse("SOCKS5://PROXY.EXAMPLE.COM:1080", 
            null, null, null, null, null, out var url11));
        Assert.AreEqual("SOCKS5", url11.Scheme);
        Assert.AreEqual("proxy.example.com", url11.Host);

        // Test alternative SOCKS5 schemes
        Assert.IsTrue(VhUrlParser.TryParse("socks://proxy.example.com", 
            null, null, null, null, null, out var url12));
        Assert.AreEqual("socks", url12.Scheme);
        Assert.AreEqual(1080, url12.Port); // Should get SOCKS5 default port

        Assert.IsTrue(VhUrlParser.TryParse("socks5h://proxy.example.com", 
            null, null, null, null, null, out var url13));
        Assert.AreEqual("socks5h", url13.Scheme);
        Assert.AreEqual(1080, url13.Port);

        // Test URL encoding in userinfo
        Assert.IsTrue(VhUrlParser.TryParse("http://user%40domain:p%40ss@proxy.example.com", 
            null, null, null, null, null, out var url14));
        var userInfo = url14.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped);
        Assert.IsTrue(userInfo.Contains("user@domain"));

        // Test invalid URLs
        Assert.IsFalse(VhUrlParser.TryParse("", null, null, null, null, null, out _));
        Assert.IsFalse(VhUrlParser.TryParse("   ", null, null, null, null, null, out _));
        Assert.IsFalse(VhUrlParser.TryParse("invalid-url", null, null, null, null, null, out _));
        
        // Test URL without host
        Assert.IsFalse(VhUrlParser.TryParse("socks5://", null, null, null, null, null, out _));
        
        // Test URL with invalid port
        Assert.IsFalse(VhUrlParser.TryParse("socks5://proxy.example.com:invalid", null, null, null, null, null, out _));
        Assert.IsFalse(VhUrlParser.TryParse("socks5://proxy.example.com:-1", null, null, null, null, null, out _));
        Assert.IsFalse(VhUrlParser.TryParse("socks5://proxy.example.com:999999", null, null, null, null, null, out _));

        // Test mixed default values
        Assert.IsTrue(VhUrlParser.TryParse("user:pass@proxy.example.com:1080", 
            ProxyProtocol.Https, 443, "fallback-user", "fallback-pass", null, out var url15));
        Assert.AreEqual("https", url15.Scheme);
        Assert.AreEqual("proxy.example.com", url15.Host);
        Assert.AreEqual(1080, url15.Port);
        // Should use URL-provided credentials, not defaults
        Assert.AreEqual("user", url15.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped).Split(':')[0]);

        // Test default port override
        Assert.IsTrue(VhUrlParser.TryParse("proxy.example.com", 
            ProxyProtocol.Http, 9090, null, null, null, out var url16));
        Assert.AreEqual("http", url16.Scheme);
        Assert.AreEqual("proxy.example.com", url16.Host);
        Assert.AreEqual(8080, url16.Port); // Should use protocol default (8080), not custom default (9090)

        // Test with only default username (no password)
        Assert.IsTrue(VhUrlParser.TryParse("proxy.example.com", 
            ProxyProtocol.Socks5, null, "only-user", null, null, out var url17));
        Assert.AreEqual("only-user", url17.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped));

        // Test complex query parameters
        Assert.IsTrue(VhUrlParser.TryParse("http://proxy.example.com?param1=value1&param2=value2", 
            null, null, null, null, false, out var url18));
        Assert.IsTrue(url18.Query.Contains("param1=value1"));
        Assert.IsTrue(url18.Query.Contains("param2=value2"));
        Assert.IsTrue(url18.Query.Contains("enabled=false"));
    }
}