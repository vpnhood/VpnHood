using VpnHood.AppLib.Api.Updaters;
using VpnHood.AppUi.Hosting.Desktop.Commands;

namespace VpnHood.AppLib.Test.Tests;

// The status command's Update line: the newer version and how far its update has come, and whether it comes by itself
[TestClass]
public class StatusCommandTest
{
    private static readonly Version NewVersion = Version.Parse("9.0.950");

    [TestMethod]
    public void The_update_line_says_each_stage()
    {
        Assert.AreEqual("9.0.950 available", Describe(AppUpdateStage.Available, isAutomaticUpdateOn: true));
        Assert.AreEqual("9.0.950 available; automatic updates are off",
            Describe(AppUpdateStage.Available, isAutomaticUpdateOn: false));
        Assert.AreEqual("downloading 9.0.950: 42%",
            Describe(AppUpdateStage.Downloading, downloadedBytes: 42, totalBytes: 100));
        Assert.AreEqual("9.0.950 downloaded, installs while the VPN is disconnected",
            Describe(AppUpdateStage.Downloaded, isAutomaticUpdateOn: true));
        Assert.AreEqual("9.0.950 downloaded; automatic updates are off",
            Describe(AppUpdateStage.Downloaded, isAutomaticUpdateOn: false));
        Assert.AreEqual("installing 9.0.950", Describe(AppUpdateStage.Installing));
        Assert.AreEqual("updating to 9.0.950 failed: No space left.",
            Describe(AppUpdateStage.Failed, errorMessage: "No space left."));
    }

    [TestMethod]
    public void A_download_of_no_known_size_says_its_bytes()
    {
        var line = Describe(AppUpdateStage.Downloading, downloadedBytes: 5_000_000, totalBytes: null);
        StringAssert.StartsWith(line, "downloading 9.0.950: ");
        Assert.IsFalse(line?.EndsWith('%'), line);
    }

    [TestMethod]
    public void No_newer_version_has_no_line()
    {
        Assert.IsNull(StatusCommand.Describe(null, isAutomaticUpdateOn: true));
        Assert.IsNull(StatusCommand.Describe(CreateStatus(VersionStatus.Latest, publishInfo: null, updateState: null),
            isAutomaticUpdateOn: true));
    }

    [TestMethod]
    public void An_app_whose_updater_is_not_the_notices_names_the_feeds_version()
    {
        var publishInfo = new PublishInfo {
            Version = NewVersion,
            UpdateInfoUrl = new Uri("https://localhost/update.json"),
            PackageUrl = new Uri("https://localhost/package"),
            InstallationPageUrl = new Uri("https://localhost/"),
            ReleaseDate = DateTime.UtcNow
        };

        Assert.AreEqual("9.0.950 available",
            StatusCommand.Describe(CreateStatus(VersionStatus.Old, publishInfo, updateState: null),
                isAutomaticUpdateOn: false));
    }

    private static string? Describe(AppUpdateStage stage, bool isAutomaticUpdateOn = true, long downloadedBytes = 0,
        long? totalBytes = null, string? errorMessage = null)
    {
        var updateState = new AppUpdateState {
            Version = NewVersion,
            Stage = stage,
            DownloadedBytes = downloadedBytes,
            TotalBytes = totalBytes,
            ErrorMessage = errorMessage
        };

        return StatusCommand.Describe(CreateStatus(VersionStatus.Old, publishInfo: null, updateState),
            isAutomaticUpdateOn);
    }

    private static AppUpdaterStatus CreateStatus(VersionStatus versionStatus, PublishInfo? publishInfo,
        AppUpdateState? updateState)
    {
        return new AppUpdaterStatus {
            CheckedTime = DateTime.UtcNow,
            VersionStatus = versionStatus,
            PublishInfo = publishInfo,
            Prompt = false,
            UpdateState = updateState
        };
    }
}
