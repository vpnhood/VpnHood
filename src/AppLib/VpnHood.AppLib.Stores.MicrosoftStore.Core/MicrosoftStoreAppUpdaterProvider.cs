using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Updaters;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using Windows.Services.Store;

namespace VpnHood.AppLib.Stores.MicrosoftStore;

/// <summary>
/// Provides the Microsoft Store's update: the Store says whether it has a newer package, and the update downloads and
/// installs it on the Store's own screen, which closes the app to put it in.
/// </summary>
/// <remarks>
/// Every call needs the desktop window, which owns the Store's dialogs, and a failure throws. An app the Store does not
/// associate with its listing fails every call (0x803F6107).
/// </remarks>
public class MicrosoftStoreAppUpdaterProvider : IAppUpdaterProvider
{
    // with the Store out of reach the check would hold the one after the connect behind it
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(15);

    private AppUpdateState _state = AppUpdateState.None;

    /// <inheritdoc/>
    /// <remarks>It is <see langword="true"/>: the Store's own screen shows the update.</remarks>
    public bool HasOwnScreen => true;

    /// <inheritdoc/>
    /// <remarks>It is <see langword="false"/>: the Store installs the update, and stops the app itself.</remarks>
    public bool IsInstalling => false;

    /// <inheritdoc/>
    /// <remarks>
    /// It asks the Store for its updates of the app: the query has 15 seconds, while making the Store's context for the
    /// window has no limit. The Store tells no date it has known an update since.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The window has no native handle.</exception>
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

    /// <inheritdoc/>
    /// <remarks>It gives the last check's state.</remarks>
    public AppUpdateState GetState(AppRelease? feedRelease)
    {
        return _state;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It asks the Store to download and install the updates on its own screen, with no time limit, and completes once the
    /// Store has ended: installed, which closes the app, or canceled by the person.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The window has no native handle, the Store has no update to show, or it could not update the app.
    /// </exception>
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

    /// <inheritdoc/>
    /// <remarks>It always throws <see cref="NotSupportedException"/>: the Store installs on its own screen.</remarks>
    public Task CompleteUpdate(AppRelease? feedRelease, IUiContext? uiContext, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The Microsoft Store installs its update on its own screen.");
    }

    /// <inheritdoc/>
    /// <remarks>It gives <see langword="false"/>: the app keeps no update of its own to resume.</remarks>
    public Task<bool> ResumeUpdate(IUiContext uiContext, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    /// <remarks>It is <see langword="false"/>: the Store keeps its own automatic update setting.</remarks>
    public bool IsAutomaticUpdateSupported => false;

    /// <inheritdoc/>
    /// <remarks>It always throws <see cref="NotSupportedException"/>.</remarks>
    public Task<bool> IsAutomaticUpdateOn(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The Microsoft Store keeps its own automatic update setting.");
    }

    /// <inheritdoc/>
    /// <remarks>It always throws <see cref="NotSupportedException"/>.</remarks>
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
