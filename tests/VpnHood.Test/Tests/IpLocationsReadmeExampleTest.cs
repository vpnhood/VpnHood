using System.Net;
using VpnHood.Net.IpLocations.Providers.Offlines;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Test.Tests;

// VpnHood.Net.IpLocations' README is a copy of the Snippet regions here, made by
// pub/lib/Update-Snippets.ps1; the NuGet publish fails when a copy differs. Edit a region, then run the script.
[TestClass]
public class IpLocationsReadmeExampleTest
{
    [TestMethod]
    [Timeout(30_000)]
    public async Task Local_lookup_and_save()
    {
        #region Snippet:IpLocationsLocal
        // IP ranges with their countries: two here; in an app, IP2Location's database (Ip2LocationDbParser)
        using var provider = new LocalIpLocationProvider([
            new LocalIpLocationProvider.IpRangeInfo {
                IpRanges = new IpRange(IPAddress.Parse("1.0.0.0"), IPAddress.Parse("1.0.0.255")),
                CountryCode = "AU"
            },
            new LocalIpLocationProvider.IpRangeInfo {
                IpRanges = new IpRange(IPAddress.Parse("5.0.0.0"), IPAddress.Parse("5.0.0.255")),
                CountryCode = "DE"
            }
        ]);

        var location = await provider.GetLocation(IPAddress.Parse("5.0.0.10"), CancellationToken.None);
        Console.WriteLine($"{location.CountryName} ({location.CountryCode})"); // Germany (DE)
        #endregion

        Assert.AreEqual("DE", location.CountryCode);

        #region Snippet:IpLocationsSaveLoad
        var path = Path.GetTempFileName(); // in an app, a file in its data folder

        // save the ranges once, in their compact binary form...
        await using (var file = File.Create(path))
            provider.Serialize(file);

        // ...and load them when the app starts
        using var loaded = LocalIpLocationProvider.Deserialize(File.OpenRead(path));
        #endregion

        var again = await loaded.GetLocation(IPAddress.Parse("1.0.0.1"), CancellationToken.None);
        Assert.AreEqual("AU", again.CountryCode);
        File.Delete(path);
    }
}
