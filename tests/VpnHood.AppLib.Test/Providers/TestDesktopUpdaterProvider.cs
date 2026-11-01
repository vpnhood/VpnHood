using VpnHood.AppUi.Hosting.Desktop.Daemon.Updaters;

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

    // The platform's installer, under way until the test ends it: with null, an install that stops the service; with
    // a message, one that installed nothing.
    public TaskCompletionSource<string?> InstallerEnd { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int InstallerRuns => _installerRuns;
    private int _installerRuns;

    // The platform's check of its switch before an install, held until the test ends it, if the test gives it one: a
    // check that ends after its cancel, as a tool already done does. Its token, for the test to see the cancel. The
    // app's own update check, made with no token, is answered at once.
    public TaskCompletionSource? CheckEnd { get; set; }
    public TaskCompletionSource CheckStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public CancellationToken CheckToken { get; private set; }

    public override async Task<bool> IsAutomaticUpdateOn(CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
            return IsOn;

        CheckToken = cancellationToken;
        CheckStarted.TrySetResult();
        if (CheckEnd is { } checkEnd)
            await checkEnd.Task;

        return IsOn;
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
        Interlocked.Increment(ref _installerRuns);
        return InstallerEnd.Task;
    }
}
