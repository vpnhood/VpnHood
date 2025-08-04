using System.Net;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.Test.Tests;

[TestClass]
public class EndPointResolverTests
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
            CancellationToken.None);

        Assert.IsFalse(results.All(ep => tokenEndPoints.Contains(ep)), "DnsOnly strategy should not include token-provided endpoints.");
    }

    [TestMethod]
    public async Task TokenOnly_ShouldUseTokenEndpoints()
    {
        var serverToken = CreateServerToken(true);
        var tokenEndPoints = serverToken.HostEndPoints ?? [];
        var results = await EndPointResolver.ResolveHostEndPoints(serverToken, EndPointStrategy.TokenOnly,
            CancellationToken.None);

        CollectionAssert.AreEqual(tokenEndPoints, results);
    }

    [TestMethod]
    public async Task DnsFirst_ShouldPrioritizeDnsEntries()
    {
        var serverToken = CreateServerToken(true);
        var tokenEndPoints = serverToken.HostEndPoints ?? [];

        var results = await EndPointResolver.ResolveHostEndPoints(serverToken, EndPointStrategy.DnsFirst,
            CancellationToken.None);

        var index = results.Length - tokenEndPoints.Length;
        Assert.AreEqual(tokenEndPoints[0], results[index + 0]);
        Assert.AreEqual(tokenEndPoints[1], results[index + 1]);
        Assert.IsTrue(tokenEndPoints.Length < results.Length, "Resolved endpoints should not be empty.");
    }

    [TestMethod]
    public async Task TokenFirst_ShouldPrioritizeTokenEndpoints()
    {
        var serverToken = CreateServerToken(true);
        var tokenEndPoints = serverToken.HostEndPoints ?? [];

        var results = await EndPointResolver.ResolveHostEndPoints(serverToken, EndPointStrategy.TokenFirst,
                CancellationToken.None);

        Assert.AreEqual(tokenEndPoints[0], results[0]);
        Assert.AreEqual(tokenEndPoints[1], results[1]);
        Assert.IsTrue(tokenEndPoints.Length < results.Length, "Resolved endpoints should not be empty.");
    }

    [TestMethod]
    public async Task Auto_ShouldNotReturnDomainEndPoints_with_invalid_hostname()
    {
        var serverToken = CreateServerToken(false);
        var tokenEndPoints = serverToken.HostEndPoints ?? [];

        var results = await EndPointResolver.ResolveHostEndPoints(serverToken, EndPointStrategy.Auto,
            CancellationToken.None);

        Assert.AreEqual(tokenEndPoints[0], results[0]);
        Assert.AreEqual(tokenEndPoints[1], results[1]);
        Assert.AreEqual(tokenEndPoints.Length, results.Length);
    }
}