using System.IO.Compression;
using System.Text;
using VpnHood.Net.IpLocations.Providers.Offlines;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class Ip2LocationDbParserTest : TestBase
{
    // The parser stamps its zip (ZipStamp), so an app reads which build it holds from the bytes in
    // front of it and opens the zip only to build from it; the zip holds the countries alone.
    [TestMethod]
    public async Task The_zip_carries_its_stamp_in_front()
    {
        const string csv = "\"16777216\",\"16777471\",\"US\",\"United States of America\"\n" +
                           "\"84017152\",\"84017407\",\"TR\",\"Turkey\"\n";
        var zipPath = Path.Combine(TestHelper.WorkingPath, $"ip-locations_{Guid.NewGuid()}.zip");
        Directory.CreateDirectory(TestHelper.WorkingPath);
        await using (var csvStream = new MemoryStream(Encoding.ASCII.GetBytes(csv)))
            await Ip2LocationDbParser.BuildLocalIpRangeLocationDb(csvStream, zipPath, TestCt);

        var zipBytes = await File.ReadAllBytesAsync(zipPath, TestCt);
        var stream = new TestForwardOnlyStream(zipBytes);
        Assert.AreEqual(16, (await ZipStamp.ReadAsync(stream, zipPath, TestCt)).Length);
        Assert.IsLessThan(100L, stream.BytesRead, "Only the stamp is read.");

        await using var archive = new ZipArchive(new MemoryStream(zipBytes));
        CollectionAssert.AreEqual(new[] { "tr.ips", "us.ips" },
            archive.Entries.Select(x => x.FullName).ToArray());
    }
}
