using VpnHood.AppLib.Abstractions.Updaters;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.Test.Providers;

/// <summary>
/// The app's own updater double, as the website APK's: no screen of its own, so the notice offers its
/// update. It records what it was asked and answers its stage.
/// </summary>
internal class TestPackageUpdaterProvider : IAppUpdaterProvider
{
    // the stage of a release it is asked about
    public AppUpdateStage Stage { get; set; } = AppUpdateStage.Available;

    // a platform that fails to read its update
    public Exception? StateError { get; set; }

    public AppRelease? LastStateRelease { get; private set; }
    public int StateCalls { get; private set; }
    public AppRelease? StartedRelease { get; private set; }
    public AppRelease? CompletedRelease { get; private set; }

    public bool HasOwnScreen => false;

    public bool IsInstalling => false;

    public Task<AppUpdateState> Check(AppRelease? feedRelease, IUiContext? uiContext,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(GetState(feedRelease));
    }

    public AppUpdateState GetState(AppRelease? feedRelease)
    {
        StateCalls++;
        LastStateRelease = feedRelease;
        if (StateError != null)
            throw StateError;

        return feedRelease is null
            ? AppUpdateState.None
            : new AppUpdateState { Stage = Stage, DownloadedBytes = 50, TotalBytes = 100 };
    }

    public Task StartUpdate(AppRelease? feedRelease, IUiContext? uiContext, CancellationToken cancellationToken)
    {
        StartedRelease = feedRelease ?? throw new InvalidOperationException("There is no newer version to download.");
        Stage = AppUpdateStage.Downloading;
        return Task.CompletedTask;
    }

    public Task CompleteUpdate(AppRelease? feedRelease, IUiContext? uiContext, CancellationToken cancellationToken)
    {
        CompletedRelease = feedRelease ?? throw new InvalidOperationException("There is no newer version to install.");
        return Task.CompletedTask;
    }

    public Task<bool> ResumeUpdate(IUiContext uiContext, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    // the platform's switch, a desktop's timer or task
    public bool IsAutomaticUpdateOnValue { get; set; } = true;

    public bool IsAutomaticUpdateSupported => true;

    public Task<bool> IsAutomaticUpdateOn(CancellationToken cancellationToken)
    {
        return Task.FromResult(IsAutomaticUpdateOnValue);
    }

    public Task SetAutomaticUpdate(bool isOn, CancellationToken cancellationToken)
    {
        IsAutomaticUpdateOnValue = isOn;
        return Task.CompletedTask;
    }
}
