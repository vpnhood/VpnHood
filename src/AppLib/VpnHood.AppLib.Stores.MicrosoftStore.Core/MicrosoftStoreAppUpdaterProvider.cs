using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Updaters;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using Windows.Services.Store;

namespace VpnHood.AppLib.Stores.MicrosoftStore;

// The Microsoft Store's own update, as GooglePlayAppUpdaterProvider is Play's: the Store says whether it has a newer
// package, and on Update downloads and installs it on its own screen, which closes the app to put it in. Every call
// needs the window, which owns the Store's dialogs; a failure throws: the service treats a failed check like the
// Store's "none", and a failed screen as one to show again. A package the Store does not know - one sideloaded, not
// installed from it - fails every call (0x803F6107).
public class MicrosoftStoreAppUpdaterProvider : IAppUpdaterProvider
{
    // with the Store out of reach the check would hold the one after the connect behind it
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(15);

    private AppUpdateState _state = AppUpdateState.None;

    public bool HasOwnScreen => true;

    // the Store installs it, and stops the app itself
    public bool IsInstalling => false;

    public async Task<AppUpdateState> Check(AppRelease? feedRelease, IUiContext? uiContext,
        CancellationToken cancellationToken)
    {
        var storeContext = await MicrosoftStoreContext.Create(uiContext ?? throw new UiContextNotAvailableException()).Vhc();
        var updates = await GetUpdates(storeContext, cancellationToken).Vhc();
        VhLogger.Instance.LogDebug("Microsoft Store updates: {Count}", updates.Count);

        // the Store tells no date it has known an update since: the service counts from the first offer it sees
        _state = updates.Count == 0 ? AppUpdateState.None : new AppUpdateState { Stage = AppUpdateStage.Available };
        return _state;
    }

    public AppUpdateState GetState(AppRelease? feedRelease)
    {
        return _state;
    }

    public async Task StartUpdate(AppRelease? feedRelease, IUiContext? uiContext, CancellationToken cancellationToken)
    {
        var storeContext = await MicrosoftStoreContext.Create(uiContext ?? throw new UiContextNotAvailableException()).Vhc();
        var updates = await GetUpdates(storeContext, cancellationToken).Vhc();
        if (updates.Count == 0)
            throw new InvalidOperationException("The Microsoft Store has no update to show.");

        // no time limit: it lasts as long as the Store's screen, and its install closes the app
        var result = await storeContext.RequestDownloadAndInstall(updates, cancellationToken).Vhc();
        VhLogger.Instance.LogInformation("Microsoft Store update finished. OverallState: {OverallState}", result.OverallState);
        if (result.OverallState is not (StorePackageUpdateState.Completed or StorePackageUpdateState.Canceled))
            throw new InvalidOperationException($"The Microsoft Store could not update the app. State: {result.OverallState}");
    }

    // the Store installs on its own screen: nothing waits for the app's Install
    public Task CompleteUpdate(AppRelease? feedRelease, IUiContext? uiContext, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The Microsoft Store installs its update on its own screen.");
    }

    // the Store's screen comes back by itself from the Store app; nothing here to resume
    public Task<bool> ResumeUpdate(IUiContext uiContext, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    public bool IsAutomaticUpdateSupported => false;

    public Task<bool> IsAutomaticUpdateOn(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The Microsoft Store keeps its own automatic update setting.");
    }

    public Task SetAutomaticUpdate(bool isOn, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The Microsoft Store keeps its own automatic update setting.");
    }

    // a TimeoutException is a failed check, which the service takes as the Store's "none"
    private static Task<IReadOnlyList<StorePackageUpdate>> GetUpdates(MicrosoftStoreContext storeContext,
        CancellationToken cancellationToken)
    {
        return storeContext.GetUpdates(cancellationToken).WaitAsync(QueryTimeout, cancellationToken);
    }
}
