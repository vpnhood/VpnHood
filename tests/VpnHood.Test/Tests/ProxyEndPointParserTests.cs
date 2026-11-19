using VpnHood.Core.Proxies.Management.Abstractions;

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
        var d3 = new ProxyEndPointDefaults
            { Protocol = ProxyProtocol.Http, Username = "default-user", Password = "default-pass" };
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
        var d5 = new ProxyEndPointDefaults
            { Protocol = ProxyProtocol.Https, Port = 443, Username = "fallback-user", Password = "fallback-pass" };
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

        // an HTTP proxy, as lists mean an IP address on 443
        Assert.AreEqual(
            new Uri("http://139.59.1.14:443/"),
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
        
        // SVG path data should not be matched as proxy
        Assert.IsNull(ProxyEndPointParser.ExtractFromText("M0 6.75C0 5.784.784 5 1.75 5h1.5a.75.75 0 0 1 0 1.5h-1.5a.25.25 0 0 0-.25.25v7.5c0 .138.112.25.25.25h7.5a.25.25 0 0 0 .25-.25v-1.5a.75.75 0 0 1 1.5 0v1.5A1.75 1.75 0 0 1 9.25 16h-7.5A1.75 1.75 0 0 1 0 14.25Z"), "SVG path data");
    }

    [TestMethod]
    public void A_disabled_endpoints_URL_reads_back_disabled()
    {
        // the URL said nothing of it, which the reader takes for enabled
        var endPoint = new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "proxy.example", Port = 1080, IsEnabled = false };
        Assert.IsFalse(ProxyEndPointParser.FromUrl(endPoint.Url).IsEnabled);

        endPoint.IsEnabled = true;
        Assert.IsTrue(ProxyEndPointParser.FromUrl(endPoint.Url).IsEnabled);
    }

    [TestMethod]
    public void The_last_use_of_an_endpoint_that_never_failed_is_its_last_success()
    {
        // it was null while there was no failure, whatever the successes
        var lastSucceeded = DateTime.UtcNow;
        Assert.AreEqual(lastSucceeded, new ProxyEndPointStatus { LastSucceeded = lastSucceeded }.LastUsed);
        Assert.AreEqual(lastSucceeded, new ProxySessionStatus { LastSucceeded = lastSucceeded }.LastUsed);

        var lastFailed = lastSucceeded.AddMinutes(1);
        Assert.AreEqual(lastFailed, new ProxyEndPointStatus { LastSucceeded = lastSucceeded, LastFailed = lastFailed }.LastUsed);
        Assert.IsNull(new ProxyEndPointStatus().LastUsed);
    }

    [TestMethod]
    public void A_success_and_a_failure_at_the_same_moment_count_as_a_failure()
    {
        // it was in neither group
        var time = DateTime.UtcNow;
        var status = new ProxyEndPointStatus { SucceededCount = 1, FailedCount = 1, LastSucceeded = time, LastFailed = time };
        Assert.IsTrue(status.IsLastUsedFailed);
        Assert.IsFalse(status.IsLastUsedSucceeded);
    }

    [TestMethod]
    public void A_downloaded_endpoint_equal_to_a_stored_one_is_kept_once()
    {
        // two objects of one endpoint were kept twice, and pushed a stored endpoint out of the limit: stored ones never
        // used come after the new ones
        var stored = new[] {
            CreateProxyEndPointInfo("proxy1.com", 1080, penalty: 0),
            CreateProxyEndPointInfo("proxy2.com", 1080, penalty: 0)
        };

        var downloaded = new[] { new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "proxy1.com", Port = 1080 } };

        var merged = ProxyEndPointUpdater.Merge(stored, downloaded, maxItemCount: 2, maxPenalty: null);
        CollectionAssert.AreEquivalent(stored.Select(x => x.EndPoint.Id).ToArray(), merged.Select(x => x.Id).ToArray());
    }

    // An HTTPS entry the old check turned off stays off, and the list's next update brings its address back as an HTTP
    // proxy, a row of its own, as a list's bare IP address on 443 now reads
    [TestMethod]
    public void A_turned_off_HTTPS_entry_stays_off_beside_the_HTTP_one_its_list_now_gives()
    {
        var stored = new[] {
            new ProxyEndPointInfo {
                EndPoint = new ProxyEndPoint {
                    Protocol = ProxyProtocol.Https, Host = "1.2.3.4", Port = 443, IsEnabled = false
                },
                Status = new ProxyEndPointStatus { ErrorMessage = "Verification of TLS connection failed." }
            }
        };

        var downloaded = ProxyEndPointParser.ExtractFromContent("1.2.3.4:443")
            .Select(ProxyEndPointParser.FromUrl)
            .ToArray();

        var merged = ProxyEndPointUpdater.Merge(stored, downloaded, maxItemCount: 10, maxPenalty: null);

        Assert.HasCount(2, merged);
        Assert.IsFalse(merged.Single(x => x.Protocol == ProxyProtocol.Https).IsEnabled);
        Assert.IsTrue(merged.Single(x => x.Protocol == ProxyProtocol.Http).IsEnabled);
    }

    [TestMethod]
    public void A_list_of_addresses_and_ports_parted_by_spaces_is_read()
    {
        // the text was cut at spaces, which parted each address from its port
        var urls = ProxyEndPointParser.ExtractFromContent("1.2.3.4 8080\n5.6.7.8 3128\n");
        CollectionAssert.AreEqual(new[] { new Uri("http://1.2.3.4:8080/"), new Uri("http://5.6.7.8:3128/") }, urls);

        // two addresses on a line are both read
        urls = ProxyEndPointParser.ExtractFromContent("socks5://1.1.1.1:1080 http://2.2.2.2:3128");
        Assert.HasCount(2, urls);
    }

    [TestMethod]
    public void An_IPv6_address_in_a_list_is_read()
    {
        // the text was cut at its brackets
        var urls = ProxyEndPointParser.ExtractFromContent("[2001:db8::1]:1080\n");
        Assert.AreEqual(new Uri("socks5://[2001:db8::1]:1080/"), urls.Single());
    }

    [TestMethod]
    public void A_port_written_is_kept_the_schemes_standard_one_too()
    {
        // a scheme's standard port was taken for one left out, and http's 80 became 8080
        Assert.AreEqual(80, ProxyEndPointParser.ParseHostToUrl("http://proxy.example:80", null).Port);
        Assert.AreEqual(8080, ProxyEndPointParser.ParseHostToUrl("http://proxy.example", null).Port);
        Assert.AreEqual(443, ProxyEndPointParser.ParseHostToUrl("https://proxy.example:443", null).Port);
    }

    [TestMethod]
    public void A_scheme_that_is_no_proxy_protocol_is_refused_with_a_port_too()
    {
        // with a port other than its own, any scheme passed
        Assert.ThrowsExactly<ArgumentException>(() => ProxyEndPointParser.ParseHostToUrl("ftp://proxy.example:1080", null));
    }

    [TestMethod]
    public void A_SOCKS_alias_is_read_as_SOCKS5()
    {
        // the URL kept the alias, which the reader refused
        var endPoint = ProxyEndPointParser.Normalize(new ProxyEndPoint {
            Protocol = ProxyProtocol.Socks5, Host = "socks://proxy.example:1080", Port = 1080
        });
        Assert.AreEqual(ProxyProtocol.Socks5, endPoint.Protocol);
    }

    [TestMethod]
    public void The_credentials_of_the_address_stay_and_the_endpoints_own_fill_what_it_leaves_out()
    {
        // the endpoint's own, null, took their place
        var endPoint = ProxyEndPointParser.Normalize(new ProxyEndPoint {
            Protocol = ProxyProtocol.Socks5, Host = "socks5://user:pass@proxy.example:1080", Port = 1080
        });
        Assert.AreEqual("user", endPoint.Username);
        Assert.AreEqual("pass", endPoint.Password);

        // an address with none takes the endpoint's own
        endPoint = ProxyEndPointParser.Normalize(new ProxyEndPoint {
            Protocol = ProxyProtocol.Socks5, Host = "proxy.example", Port = 1080, Username = " own ", Password = "secret"
        });

        Assert.AreEqual("own", endPoint.Username);
        Assert.AreEqual("secret", endPoint.Password);
    }

    [TestMethod]
    public void Credentials_with_URL_characters_come_back_as_they_were()
    {
        // they went into the URL as they were, where a '/', '?' or '#' ends its address
        var endPoint = new ProxyEndPoint {
            Protocol = ProxyProtocol.Socks5, Host = "proxy.example", Port = 1080, Username = "u:x", Password = "p/#?%41"
        };

        var read = ProxyEndPointParser.FromUrl(endPoint.Url);
        Assert.AreEqual(endPoint.Username, read.Username);
        Assert.AreEqual(endPoint.Password, read.Password);

        read = ProxyEndPointParser.Normalize(new ProxyEndPoint {
            Protocol = ProxyProtocol.Socks5, Host = "proxy.example", Port = 1080, Username = "u", Password = "a/b#c"
        });

        Assert.AreEqual("a/b#c", read.Password);
    }

    [TestMethod]
    public void Port_443_goes_to_the_default_scheme_where_HTTPS_is_not_preferred()
    {
        // the choice changed nothing; a host name, as an IP address on 443 is an HTTP proxy either way
        Assert.AreEqual(new Uri("http://proxy.example:443/"),
            ProxyEndPointParser.ExtractFromText("proxy.example:443", preferHttpsWhenPort443: false));
        Assert.AreEqual(new Uri("https://proxy.example:443/"), ProxyEndPointParser.ExtractFromText("proxy.example:443"));
    }

    // An HTTPS proxy needs a certificate the device trusts for its address, which one at an IP address almost never
    // has, and lists write HTTPS for an HTTP proxy that carries HTTPS: such entries were HTTPS proxies that always failed
    [TestMethod]
    public void A_lists_HTTPS_entry_at_an_IP_address_with_no_user_name_is_an_HTTP_proxy()
    {
        var urls = ProxyEndPointParser.ExtractFromContent(
            "https://1.2.3.4:8080\n" + // written, as the list the website suggests has it
            "5.6.7.8:443\n" + // guessed from the port
            "9.9.9.9 3128 HTTPS\n" + // a table's protocol column
            "[2001:db8::1]:443\n" +
            "https://user:pass@1.2.3.4:443\n" + // its password goes inside the proxy's TLS alone
            "proxy.example:443\n" + // a name may have a certificate
            "https://proxy.example:8443");

        CollectionAssert.AreEqual(new[] {
            "http://1.2.3.4:8080/",
            "http://5.6.7.8:443/",
            "http://9.9.9.9:3128/",
            "http://[2001:db8::1]:443/",
            "https://user:pass@1.2.3.4/",
            "https://proxy.example/",
            "https://proxy.example:8443/"
        }, urls.Select(x => x.AbsoluteUri).ToArray());
    }

    [TestMethod]
    public void A_URL_found_in_a_text_keeps_its_query_and_its_SOCKS4_credentials()
    {
        // the query was dropped, and a disabled endpoint came back enabled
        var url = ProxyEndPointParser.ExtractFromText("socks5://proxy.example:1080/?enabled=0");
        Assert.IsNotNull(url);
        Assert.IsFalse(ProxyEndPointParser.FromUrl(url).IsEnabled);

        // a SOCKS4 URL was not one, and its user was lost
        url = ProxyEndPointParser.ExtractFromText("socks4://user@1.2.3.4:1080");
        Assert.IsNotNull(url);
        var endPoint = ProxyEndPointParser.FromUrl(url);
        Assert.AreEqual(ProxyProtocol.Socks4, endPoint.Protocol);
        Assert.AreEqual("user", endPoint.Username);
    }

    [TestMethod]
    public void ProxyEndPointUpdater_BasicMerge()
    {
        // Create some existing endpoints with different penalties
        var existing = new[] {
            CreateProxyEndPointInfo("proxy1.com", 1080, penalty: 5, lastUsedTime: DateTime.UtcNow.AddMinutes(-5)),
            CreateProxyEndPointInfo("proxy2.com", 1080, penalty: 0, lastUsedTime: DateTime.UtcNow.AddMinutes(-10)),
            CreateProxyEndPointInfo("proxy3.com", 1080, penalty: 10, lastUsedTime: DateTime.UtcNow.AddMinutes(-15))
        };

        // Create new endpoints
        var newEndPoints = new[] {
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "newproxy1.com", Port = 1080 },
            new ProxyEndPoint { Protocol = ProxyProtocol.Http, Host = "newproxy2.com", Port = 8080 }
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
        var existing = new[] {
            CreateProxyEndPointInfo("proxy1.com", 1080, penalty: 5, lastUsedTime: DateTime.UtcNow),
            CreateProxyEndPointInfo("proxy2.com", 1080, penalty: 3, lastUsedTime: DateTime.UtcNow),
            CreateProxyEndPointInfo("proxy3.com", 1080, penalty: 8, lastUsedTime: DateTime.UtcNow)
        };

        var newEndPoints = new[] {
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "newproxy1.com", Port = 1080 },
            new ProxyEndPoint { Protocol = ProxyProtocol.Http, Host = "newproxy2.com", Port = 8080 },
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "newproxy3.com", Port = 1081 }
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
        var existing = new[] {
            CreateProxyEndPointInfo("proxy1.com", 1080, penalty: 5, lastUsedTime: DateTime.UtcNow),
            CreateProxyEndPointInfo("proxy2.com", 1080, penalty: 3, lastUsedTime: DateTime.UtcNow)
        };

        // New list contains one duplicate
        var newEndPoints = new[] {
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "proxy1.com", Port = 1080 },
            new ProxyEndPoint { Protocol = ProxyProtocol.Http, Host = "newproxy.com", Port = 8080 }
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
        var existing = new[] {
            CreateProxyEndPointInfo("proxy1.com", 1080, penalty: 5)
        };
        var newEndPoints = new[] {
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "newproxy.com", Port = 1080 }
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
        var newEndPoints = new[] {
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "newproxy1.com", Port = 1080 },
            new ProxyEndPoint { Protocol = ProxyProtocol.Http, Host = "newproxy2.com", Port = 8080 }
        };

        var merged = ProxyEndPointUpdater.Merge(existing, newEndPoints, maxItemCount: 10, maxPenalty: 0);

        Assert.HasCount(2, merged);
        Assert.IsTrue(merged.Any(p => p.Host == "newproxy1.com"));
        Assert.IsTrue(merged.Any(p => p.Host == "newproxy2.com"));
    }

    [TestMethod]
    public void ProxyEndPointUpdater_AllExistingWithHighPenalty()
    {
        var existing = new[] {
            CreateProxyEndPointInfo("proxy1.com", 1080, penalty: 5, lastUsedTime: DateTime.UtcNow),
            CreateProxyEndPointInfo("proxy2.com", 1080, penalty: 8, lastUsedTime: DateTime.UtcNow)
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
        var existing = new[] {
            CreateProxyEndPointInfo("used-good.com", 1080, penalty: 5,
                lastUsedTime: DateTime.UtcNow), // actually bad for maxPenalty=0
            CreateProxyEndPointInfo("used-bad.com", 1080, penalty: -5,
                lastUsedTime: DateTime.UtcNow), // actually good for maxPenalty=0
            CreateProxyEndPointInfo("unused.com", 1080, penalty: 10, lastUsedTime: null)
        };

        var newEndPoints = new[] {
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "new.com", Port = 1080 }
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
        var existing = new[] {
            CreateProxyEndPointInfo("used1.com", 1080, penalty: 10, lastUsedTime: DateTime.UtcNow),
            CreateProxyEndPointInfo("used2.com", 1080, penalty: 5, lastUsedTime: DateTime.UtcNow),
            CreateProxyEndPointInfo("used3.com", 1080, penalty: -2, lastUsedTime: DateTime.UtcNow),
            CreateProxyEndPointInfo("used4.com", 1080, penalty: 8, lastUsedTime: DateTime.UtcNow)
        };

        var newEndPoints = new[] {
            new ProxyEndPoint { Protocol = ProxyProtocol.Socks5, Host = "new.com", Port = 1080 }
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

    [TestMethod]
    public void A_port_written_in_a_list_is_kept_the_schemes_standard_one_too()
    {
        // the app's import and a list's download read a URL by the extractor, which took 80 for no port: 8080
        var url = ProxyEndPointParser.ExtractFromContent("http://proxy.example:80").Single();
        Assert.AreEqual(80, ProxyEndPointParser.FromUrl(url).Port);
    }

    [TestMethod]
    public void A_port_of_0_or_of_six_digits_is_no_address()
    {
        // 0 took the default port, and the sixth digit was cut off
        Assert.IsEmpty(ProxyEndPointParser.ExtractFromContent("http://proxy.example:0"));
        Assert.IsEmpty(ProxyEndPointParser.ExtractFromContent("http://proxy.example:123456"));
        Assert.ThrowsExactly<ArgumentException>(() => ProxyEndPointParser.ParseHostToUrl("http://proxy.example:0", null));
    }

    [TestMethod]
    public void A_protocol_named_before_an_address_in_a_list_is_its_protocol()
    {
        // the name was a word of its own, and the address took the port's guess
        var url = ProxyEndPointParser.ExtractFromContent("SOCKS4 1.2.3.4 8080").Single();
        Assert.AreEqual(ProxyProtocol.Socks4, ProxyEndPointParser.FromUrl(url).Protocol);
    }

    [TestMethod]
    public void A_protocol_named_after_an_address_in_a_list_is_its_protocol()
    {
        // tables give it after the port: the address took the port's guess
        var url = ProxyEndPointParser.ExtractFromContent("1.2.3.4 4145 SOCKS4").Single();
        Assert.AreEqual(ProxyProtocol.Socks4, ProxyEndPointParser.FromUrl(url).Protocol);
    }

    [TestMethod]
    public void A_space_at_the_end_of_a_password_in_an_address_is_kept()
    {
        // the address's credentials were trimmed as the typed ones are
        var endPoint = ProxyEndPointParser.Normalize(new ProxyEndPoint {
            Protocol = ProxyProtocol.Socks5, Host = "socks5://user:pass%20@proxy.example:1080", Port = 1080
        });
        Assert.AreEqual("pass ", endPoint.Password);
    }

    [TestMethod]
    public void The_scheme_unknown_is_no_proxy_protocol()
    {
        // it was the parser's own mark for an address with no scheme, so a default protocol took its place
        Assert.ThrowsExactly<ArgumentException>(() => ProxyEndPointParser.ParseHostToUrl("unknown://proxy.example:1080",
            new ProxyEndPointDefaults { Protocol = ProxyProtocol.Socks5 }));
    }

    // Helper method to create ProxyEndPointInfo with custom penalty and LastUsedTime
    private static ProxyEndPointInfo CreateProxyEndPointInfo(string host, int port, int penalty,
        DateTime? lastUsedTime = null)
    {
        return new ProxyEndPointInfo {
            EndPoint = new ProxyEndPoint {
                Protocol = ProxyProtocol.Socks5,
                Host = host,
                Port = port
            },
            Status = new ProxyEndPointStatus {
                Penalty = penalty,
                LastSucceeded = lastUsedTime,
                SucceededCount = lastUsedTime != null ? 1 : 0 // make sure has used
            }
        };
    }
}