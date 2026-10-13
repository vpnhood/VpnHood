using VpnHood.AppLib.Abstractions.Updaters;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Common.Exceptions;

namespace VpnHood.AppLib.Test.Providers;

/// <summary>A store's updater double: it shows its own screen, so every call needs the window.</summary>
internal class TestAppUpdaterProvider : IAppUpdaterProvider
{
    public int CheckCalls { get; private set; }
    public int StartUpdateCalls { get; private set; }

    // What the store answers. False also stands for a store out of reach: Play answers "none" then.
    public bool IsAvailable { get; set; }

    // The day the store learned of the update, where it names one (Play).
    public DateTime? AvailableSince { get; set; }

    // What its screen answers: false for one that failed.
    public bool IsUpdateShown { get; set; } = true;

    // An update the person started and left before it finished.
    public bool IsUpdateInProgress { get; set; }
    public int ResumeUpdateCalls { get; private set; }

    // A store slow to answer: the answer waits for this task.
    public Task? AnswerWait { get; set; }

    public bool HasOwnScreen => true;

    public async Task<AppUpdateState> Check(AppRelease? feedRelease, IUiContext? uiContext,
        CancellationToken cancellationToken)
    {
        if (uiContext is null)
            throw new UiContextNotAvailableException();

        CheckCalls++;
        if (AnswerWait != null)
            await AnswerWait.WaitAsync(cancellationToken);

        return GetState(feedRelease);
    }

    public AppUpdateState GetState(AppRelease? feedRelease)
    {
        return IsAvailable
            ? new AppUpdateState { Stage = AppUpdateStage.Available, AvailableSince = AvailableSince }
            : AppUpdateState.None;
    }

    // the store's screen
    public Task StartUpdate(AppRelease? feedRelease, IUiContext? uiContext, CancellationToken cancellationToken)
    {
        if (uiContext is null)
            throw new UiContextNotAvailableException();

        StartUpdateCalls++;
        return IsUpdateShown
            ? Task.CompletedTask
            : Task.FromException(new InvalidOperationException("The store's screen failed."));
    }

    public Task CompleteUpdate(AppRelease? feedRelease, IUiContext? uiContext, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The store installs on its own screen.");
    }

    // the screen of an update in progress, brought back
    public Task<bool> ResumeUpdate(IUiContext uiContext, CancellationToken cancellationToken)
    {
        if (!IsUpdateInProgress)
            return Task.FromResult(false);

        ResumeUpdateCalls++;
        return Task.FromResult(true);
    }
}
