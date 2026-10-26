using VpnHood.AppLib.Abstractions.Updaters;
using VpnHood.AppLib.Test.Providers;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.Test.Tests;

// The notice's Update on a desktop installed from our site (DesktopAppUpdaterProvider's own download): a cut download
// keeps its .part, which the next Update continues by a range request; a server that ignores ranges starts it over.
[TestClass]
public class DesktopUpdateDownloadTest : TestAppBase
{
    [TestMethod]
    public async Task A_cut_download_is_continued_where_it_stopped()
    {
        var package = CreatePackage();
        using var server = new TestPackageServer(package) { CutAfterBytes = package.Length / 3 };
        var updater = CreateUpdater();
        var release = new AppRelease { Version = new Version(1, 0, 1), PackageUrl = server.PackageUrl };

        await Download(updater, release, AppUpdateStage.Failed);
        Assert.AreEqual(package.Length / 3, new FileInfo(GetPackagePath(updater, release) + ".part").Length,
            "the cut part, kept");

        await Download(updater, release, AppUpdateStage.Downloaded);
        CollectionAssert.AreEqual(new[] { null, $"bytes={package.Length / 3}-" }, server.Ranges.ToArray());
        CollectionAssert.AreEqual(package, await File.ReadAllBytesAsync(GetPackagePath(updater, release), TestCt));
    }

    [TestMethod]
    public async Task A_server_that_ignores_ranges_starts_it_over()
    {
        var package = CreatePackage();
        using var server = new TestPackageServer(package) { CutAfterBytes = package.Length / 3 };
        var updater = CreateUpdater();
        var release = new AppRelease { Version = new Version(1, 0, 1), PackageUrl = server.PackageUrl };
        await Download(updater, release, AppUpdateStage.Failed);

        server.IsRangeIgnored = true;
        await Download(updater, release, AppUpdateStage.Downloaded);
        CollectionAssert.AreEqual(package, await File.ReadAllBytesAsync(GetPackagePath(updater, release), TestCt));
    }

    // cut after its last byte, before its rename: the server has no more for it, and it comes again whole
    [TestMethod]
    public async Task A_part_as_long_as_the_package_is_downloaded_again()
    {
        var package = CreatePackage();
        using var server = new TestPackageServer(package);
        var updater = CreateUpdater();
        var release = new AppRelease { Version = new Version(1, 0, 1), PackageUrl = server.PackageUrl };
        Directory.CreateDirectory(updater.UpdatesPath);
        await File.WriteAllBytesAsync(GetPackagePath(updater, release) + ".part", package, TestCt);

        await Download(updater, release, AppUpdateStage.Downloaded);
        CollectionAssert.AreEqual(new[] { $"bytes={package.Length}-", null }, server.Ranges.ToArray());
        CollectionAssert.AreEqual(package, await File.ReadAllBytesAsync(GetPackagePath(updater, release), TestCt));
    }

    [TestMethod]
    public async Task The_cut_download_of_an_overtaken_release_goes()
    {
        var package = CreatePackage();
        using var server = new TestPackageServer(package);
        var updater = CreateUpdater();
        var overtaken = new AppRelease { Version = new Version(1, 0, 1), PackageUrl = server.PackageUrl };
        var release = new AppRelease { Version = new Version(1, 0, 2), PackageUrl = server.PackageUrl };
        Directory.CreateDirectory(updater.UpdatesPath);
        var overtakenPartPath = GetPackagePath(updater, overtaken) + ".part";
        await File.WriteAllBytesAsync(overtakenPartPath, package[..1000], TestCt);

        await Download(updater, release, AppUpdateStage.Downloaded);
        Assert.IsFalse(File.Exists(overtakenPartPath));
    }

    // an Update to its end, the stage it ends at
    private async Task Download(TestDesktopUpdaterProvider updater, AppRelease release, AppUpdateStage stage)
    {
        await updater.StartUpdate(release, null, TestCt);
        await VhTestUtil.AssertEqualsWait(stage, () => updater.GetState(release).Stage, cancellationToken: TestCt);
    }

    private static string GetPackagePath(TestDesktopUpdaterProvider updater, AppRelease release)
    {
        return Path.Combine(updater.UpdatesPath, $"{release.Version}-Package.bin");
    }

    private static byte[] CreatePackage()
    {
        var package = new byte[300_000];
        Random.Shared.NextBytes(package);
        return package;
    }

    // each test's own install folder
    private TestDesktopUpdaterProvider CreateUpdater()
    {
        var folderPath = Directory.CreateDirectory(Path.Combine(TestHelper.WorkingPath, $"desktop-{Guid.NewGuid():N}"))
            .FullName;
        return new TestDesktopUpdaterProvider(folderPath);
    }
}
