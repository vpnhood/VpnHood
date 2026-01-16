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
        // build db from Ip2LocationLiteDb

        var dbFile = @"C:\Users\User\Downloads\IpLocations.db";
        var memoryStream = new MemoryStream(Ip2LocationLiteDb.ZipData);
        await IpLocationSqliteBuilder.Build(memoryStream, dbFile);
        await using var ipLocationSqliteProvider = await IpLocationSqliteProvider.Open(dbFile);

        var list = new List<string>();
        var ranges = await ipLocationSqliteProvider.GetIpRanges("tr");
        Console.WriteLine(ranges.Count);

        var z = new LocalIpRangeLocationProvider(
            () => new ZipArchive(new MemoryStream(Ip2LocationLiteDb.ZipData)),
            () => null);
        var trRanges = await z.GetIpRanges("TR");
        Console.WriteLine(trRanges.Count);
        Console.WriteLine(ranges.SequenceEqual(trRanges));


        // get country by ip using provider
        var ipToCheck = IPAddress.Parse("75.63.95.93");
        var ipLocation = await ipLocationSqliteProvider.GetLocation(ipToCheck, CancellationToken.None);
        Console.WriteLine($"IP {ipToCheck} belongs to country: {ipLocation.CountryCode}");

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