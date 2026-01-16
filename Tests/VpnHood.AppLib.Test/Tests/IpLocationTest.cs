using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Assets.Ip2LocationLite;
using VpnHood.Core.IpLocations.Providers.Offlines;
using VpnHood.Core.IpLocations.SqliteProvider;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
public class IpLocationTest : TestAppBase
{
    [TestMethod]
    public async Task CheckSqliteFromLocationFile()
    {
        var sqliteConnectionString = new SqliteConnectionStringBuilder {
            DataSource = "file:IpLocations?mode=memory&cache=shared",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        await using var keepAliveConnection = new SqliteConnection(sqliteConnectionString);
        await keepAliveConnection.OpenAsync();

        await using (var memoryStream = new MemoryStream(Ip2LocationLiteDb.ZipData)) {
            await IpLocationSqliteBuilder.Build(memoryStream, keepAliveConnection);
        }

        await using var ipLocationSqliteProvider = await IpLocationSqliteProvider.Open(keepAliveConnection, leaveOpen: true);
        using var localRangeProvider = new LocalIpRangeLocationProvider(
            () => new ZipArchive(new MemoryStream(Ip2LocationLiteDb.ZipData)),
            () => null);

        // compare ip ranges for a country
        var sqliteRanges = await ipLocationSqliteProvider.GetIpRanges("TR");
        var localRanges = await localRangeProvider.GetIpRanges("TR");
        Assert.IsTrue(sqliteRanges.SequenceEqual(localRanges), "SQLite ranges differ from local ranges for TR.");

        // get country by ip using provider
        var ipToCheck = IPAddress.Parse("75.63.95.93");
        var sqliteLocation = await ipLocationSqliteProvider.GetLocation(ipToCheck, CancellationToken.None);
        var localLocation = await localRangeProvider.GetLocation(ipToCheck, CancellationToken.None);
        Assert.AreEqual(sqliteLocation.CountryCode, localLocation.CountryCode, "Country code mismatch between providers.");

        // verify ip is in country ranges
        var sqliteCountryRanges = await ipLocationSqliteProvider.GetIpRanges(sqliteLocation.CountryCode);
        Assert.IsTrue(sqliteCountryRanges.Any(x => x.IsInRange(ipToCheck)), "SQLite provider ranges do not contain test IP.");
    }

    private async Task UpdateIp2LocationFile()
    {
        // update current ipLocation in app project after a week
        var vhFolder = TestHelper.GetParentDirectory(Directory.GetCurrentDirectory(), 6);
        var solutionFolder = Path.Combine(vhFolder, "VpnHood.AppLib.Assets.IpLocations");
        var projectFolder = Path.Combine(solutionFolder, "VpnHood.AppLib.Assets.Ip2LocationLite");
        var ipLocationFile = Path.Combine(projectFolder, "Resources", "IpLocations.zip");
        VhLogger.Instance.LogInformation("ipLocationFile: {ipLocationFile}", ipLocationFile);
        if (!Directory.Exists(projectFolder))
            throw new DirectoryNotFoundException("Ip2Location Project was not found.");

        // find token
        var userSecretFile = Path.Combine(vhFolder, ".user", "credentials.json");
        var document = JsonDocument.Parse(await File.ReadAllTextAsync(userSecretFile));
        var ip2LocationToken = document.RootElement.GetProperty("Ip2LocationToken").GetString();
        ArgumentException.ThrowIfNullOrWhiteSpace(ip2LocationToken);

        await Ip2LocationDbParser.UpdateLocalDb(ipLocationFile, ip2LocationToken, forIpRange: true);

        // commit project and sync
        try {
            var gitBase = $"--git-dir=\"{solutionFolder}/.git\" --work-tree=\"{solutionFolder}\"";
            await OsUtils.ExecuteCommandAsync("git", $"{gitBase} commit -a -m Publish", CancellationToken.None);
            await OsUtils.ExecuteCommandAsync("git", $"{gitBase} pull", CancellationToken.None);
            await OsUtils.ExecuteCommandAsync("git", $"{gitBase} push", CancellationToken.None);
        }
        catch (ExternalException ex) when (ex.ErrorCode == 1) {
            VhLogger.Instance.LogInformation("Nothing has been updated.");
        }
    }

    [TestMethod]
    public async Task IpLocations_must_be_loaded()
    {
        await UpdateIp2LocationFile();

        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.UseInternalLocationService = true;
        await using var app = TestAppHelper.CreateClientApp(appOptions: appOptions);
        var countryCodes = await app.IpRangeLocationProvider.GetCountryCodes();
        Assert.IsTrue(countryCodes.Any(x => x == "US"),
            "Countries has not been extracted.");

        // make sure GetIpRange works
        Assert.IsTrue((await app.IpRangeLocationProvider.GetIpRanges("US")).Any());
    }
}