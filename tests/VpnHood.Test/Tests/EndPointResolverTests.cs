using System.Net;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Tokens;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class EndPointResolverTests : TestBase
{
    private static ServerToken CreateServerToken(bool isValidDomain)
    {
        var randomId = Guid.NewGuid();
        var serverToken = new ServerToken {
            HostEndPoints = [
                IPEndPoint.Parse("192.168.1.1:443"),
                IPEndPoint.Parse("192.168.1.2:443")
            ],
            CertificateHash = randomId.ToByteArray(),
            HostName = "vpnhood.com",
            HostPort = 443,
            Secret = randomId.ToByteArray(),
            CreatedTime = DateTime.UtcNow,
            IsValidHostName = isValidDomain
        };
        return serverToken;
    }

    [TestMethod]
    public async Task DnsOnly_ShouldResolveDnsEntries()
    {
        var serverToken = CreateServerToken(true);
        var tokenEndPoints = serverToken.HostEndPoints ?? [];
        var results = await EndPointResolver.ResolveHostEndPoints(serverToken, EndPointStrategy.DnsOnly,
            TestCt);

        Assert.IsFalse(results.All(ep => tokenEndPoints.Contains(ep)),
            "DnsOnly strategy should not include token-provided endpoints.");
    }

    // Under the kill switch's block the name is asked of the network's resolvers from this process, as the OS's
    // resolver asks from a process of its own, which the block stops.
    [TestMethod]
    public async Task DnsOnly_by_own_resolver_resolves_the_name()
    {
        var serverToken = CreateServerToken(true);
        var tokenEndPoints = serverToken.HostEndPoints ?? [];
        var results = await EndPointResolver.ResolveHostEndPoints(serverToken, EndPointStrategy.DnsOnly,
            TestCt, ownResolverSocketFactory: new TestSocketFactory());

        Assert.IsNotEmpty(results);
        Assert.IsFalse(results.Any(ep => tokenEndPoints.Contains(ep)));
    }

    [TestMethod]
    public async Task TokenOnly_ShouldUseTokenEndpoints()
    {
        var serverToken = CreateServerToken(true);
        var tokenEndPoints = serverToken.HostEndPoints ?? [];
        var results = await EndPointResolver.ResolveHostEndPoints(serverToken, EndPointStrategy.IpOnly,
            TestCt);

        CollectionAssert.AreEqual(tokenEndPoints, results);
    }

    [TestMethod]
    public async Task DnsFirst_ShouldPrioritizeDnsEntries()
    {
        var serverToken = CreateServerToken(true);
        var tokenEndPoints = serverToken.HostEndPoints ?? [];

        var results = await EndPointResolver.ResolveHostEndPoints(serverToken, EndPointStrategy.DnsFirst,
            TestCt);

        var index = results.Length - tokenEndPoints.Length;
        Assert.AreEqual(tokenEndPoints[0], results[index + 0]);
        Assert.AreEqual(tokenEndPoints[1], results[index + 1]);
        Assert.IsGreaterThan(tokenEndPoints.Length, results.Length, "Resolved endpoints should not be empty.");
    }

    [TestMethod]
    public async Task TokenFirst_ShouldPrioritizeTokenEndpoints()
    {
        var serverToken = CreateServerToken(true);
        var tokenEndPoints = serverToken.HostEndPoints ?? [];

        var results = await EndPointResolver.ResolveHostEndPoints(serverToken, EndPointStrategy.IpFirst,
            TestCt);

        Assert.AreEqual(tokenEndPoints[0], results[0]);
        Assert.AreEqual(tokenEndPoints[1], results[1]);
        Assert.IsGreaterThan(tokenEndPoints.Length, results.Length, "Resolved endpoints should not be empty.");
    }

    [TestMethod]
    public async Task Auto_ShouldNotReturnDomainEndPoints_with_invalid_hostname()
    {
        var serverToken = CreateServerToken(false);
        var tokenEndPoints = serverToken.HostEndPoints ?? [];

        var results = await EndPointResolver.ResolveHostEndPoints(serverToken, EndPointStrategy.Auto,
            TestCt);

        Assert.AreEqual(tokenEndPoints[0], results[0]);
        Assert.AreEqual(tokenEndPoints[1], results[1]);
        Assert.HasCount(tokenEndPoints.Length, results);
    }
}