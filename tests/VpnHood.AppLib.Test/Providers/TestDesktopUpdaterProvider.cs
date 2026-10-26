using VpnHood.AppUi.Hosting.Desktop.Updaters;

namespace VpnHood.AppLib.Test.Providers;

/// <summary>
/// A desktop install's updater double: the platform's switch and its run-now are recorded, not made, and the
/// updater's marker of a missed run and the updates folder are in the folder given; the download is the app's own.
/// </summary>
internal sealed class TestDesktopUpdaterProvider(string folderPath)
    : DesktopAppUpdaterProvider(Path.Combine(folderPath, "updates"), Path.Combine(folderPath, MissedFileName),
        new Uri("https://localhost/feed.json"), new Version(1, 0, 0))
{
    public string MissedPath { get; } = Path.Combine(folderPath, MissedFileName);
    public string UpdatesPath { get; } = Path.Combine(folderPath, "updates");
    public bool IsOn { get; set; } = true;
    public int RunCalls { get; private set; }

    // a platform whose updater cannot be started
    public Exception? RunError { get; set; }

    public override Task<bool> IsAutomaticUpdateOn(CancellationToken cancellationToken)
    {
        return Task.FromResult(IsOn);
    }

    public override Task SetAutomaticUpdate(bool isOn, CancellationToken cancellationToken)
    {
        IsOn = isOn;
        return Task.CompletedTask;
    }

    protected override Task RunUpdater(CancellationToken cancellationToken)
    {
        RunCalls++;
        return RunError is null ? Task.CompletedTask : Task.FromException(RunError);
    }

    protected override Task<string?> RunInstaller(string packagePath)
    {
        throw new NotSupportedException("The double installs nothing.");
    }
}
