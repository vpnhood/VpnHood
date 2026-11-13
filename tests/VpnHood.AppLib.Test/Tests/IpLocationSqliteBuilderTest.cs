using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using VpnHood.Net.IpLocations.Providers.Offlines;
using VpnHood.Net.IpLocations.Providers.SqliteProvider;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Test.Providers;

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
public class IpLocationSqliteBuilderTest : TestAppBase
{
    // From a zip that is read forward only, as an Android asset is: the first build takes it all, and
    // a build over the db that holds its stamp reads the stamp and stops.
    [TestMethod]
    public async Task A_db_that_holds_the_stamp_reads_no_further()
    {
        const string csv = "\"16777216\",\"16777471\",\"US\",\"United States of America\"\n" +
                           "\"84017152\",\"84017407\",\"TR\",\"Turkey\"\n";
        Directory.CreateDirectory(TestAppHelper.WorkingPath);
        var zipPath = Path.Combine(TestAppHelper.WorkingPath, $"ip-locations_{Guid.NewGuid()}.zip");
        await using (var csvStream = new MemoryStream(Encoding.ASCII.GetBytes(csv)))
            await Ip2LocationDbParser.BuildLocalIpRangeLocationDb(csvStream, zipPath, TestCt);

        var zip = new ForwardOnlyAsset(await File.ReadAllBytesAsync(zipPath, TestCt));
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await IpLocationSqliteBuilder.Build(zip, connection, TestCt);

        await using var provider = await IpLocationSqliteProvider.Open(connection, leaveOpen: true);
        Assert.AreEqual("TR", (await provider.GetLocation(IPAddress.Parse("5.2.0.1"), TestCt)).CountryCode);

        var bytesRead = zip.BytesRead;
        await IpLocationSqliteBuilder.Build(zip, connection, TestCt);
        Assert.IsLessThan(100L, zip.BytesRead - bytesRead, "Only the stamp is read.");
    }

    // A rebuild is one transaction, the tables' drop included: one that fails part-way leaves the last build.
    [TestMethod]
    public async Task A_rebuild_that_fails_keeps_the_last_build()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await IpLocationSqliteBuilder.Build(new ForwardOnlyAsset(await BuildRangeZip()), connection, TestCt);

        // another zip, whose one country's ranges are cut short: its import fails after the rebuild began
        using var badZip = new MemoryStream();
        await using (var archive = new ZipArchive(badZip, ZipArchiveMode.Create, leaveOpen: true))
        await using (var entryStream = await archive.CreateEntry("zz.ips").OpenAsync(TestCt))
            entryStream.WriteByte(1);

        await Assert.ThrowsExactlyAsync<EndOfStreamException>(() =>
            IpLocationSqliteBuilder.Build(new ForwardOnlyAsset(badZip.ToArray()), connection, TestCt));

        await using var provider = await IpLocationSqliteProvider.Open(connection, leaveOpen: true);
        Assert.AreEqual("TR", (await provider.GetLocation(IPAddress.Parse("5.2.0.1"), TestCt)).CountryCode);
    }

    // a database named without a folder is the current folder's: there is none to make
    [TestMethod]
    public async Task A_bare_file_name_builds_in_the_current_folder()
    {
        var dbName = $"ip-locations_{Guid.NewGuid():N}.db";
        try {
            await IpLocationSqliteBuilder.Build(new ForwardOnlyAsset(await BuildRangeZip()), dbName, TestCt);
            Assert.IsTrue(File.Exists(dbName));
        }
        finally {
            SqliteConnection.ClearAllPools(); // the pool keeps the file open
            File.Delete(dbName);
        }
    }

    private async Task<byte[]> BuildRangeZip()
    {
        const string csv = "\"16777216\",\"16777471\",\"US\",\"United States of America\"\n" +
                           "\"84017152\",\"84017407\",\"TR\",\"Turkey\"\n";
        Directory.CreateDirectory(TestAppHelper.WorkingPath);
        var zipPath = Path.Combine(TestAppHelper.WorkingPath, $"ip-locations_{Guid.NewGuid()}.zip");
        await using (var csvStream = new MemoryStream(Encoding.ASCII.GetBytes(csv)))
            await Ip2LocationDbParser.BuildLocalIpRangeLocationDb(csvStream, zipPath, TestCt);

        return await File.ReadAllBytesAsync(zipPath, TestCt);
    }

    // an asset that can only be read forward, counting what is read of it
    private class ForwardOnlyAsset(byte[] bytes) : IAsset
    {
        private readonly List<TestForwardOnlyStream> _streams = [];
        public long BytesRead => _streams.Sum(x => x.BytesRead);

        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            var stream = new TestForwardOnlyStream(bytes);
            _streams.Add(stream);
            return Task.FromResult<Stream>(stream);
        }
    }
}
