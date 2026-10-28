using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Net.Toolkit.Extensions;
using Windows.Services.Store;
using WinRT.Interop;

namespace VpnHood.AppLib.Stores.MicrosoftStore;

// The Store's context for the person, its dialogs owned by the app's window and its calls made on the window's
// thread: a desktop app's StoreContext with no owner window answers wrongly or fails (Microsoft Learn, "Using the
// StoreContext class with the Desktop Bridge"), and a call off the UI thread fails with 0x80070578 - the reference
// asks the UI thread of every call that opens a dialog, and names the error for the update query too. A call that
// never runs, its window gone, ends with the caller's token.
internal sealed class MicrosoftStoreContext
{
    private readonly IWindowHandleUiContext _uiContext;
    private readonly StoreContext _storeContext;

    private MicrosoftStoreContext(IWindowHandleUiContext uiContext, StoreContext storeContext)
    {
        _uiContext = uiContext;
        _storeContext = storeContext;
    }

    public static async Task<MicrosoftStoreContext> Create(IUiContext uiContext)
    {
        var windowUiContext = uiContext as IWindowHandleUiContext ??
                              throw new NotSupportedException("The Microsoft Store needs a window to own its dialogs.");
        var storeContext = StoreContext.GetDefault();
        InitializeWithWindow.Initialize(storeContext, await windowUiContext.GetWindowHandle().Vhc());
        return new MicrosoftStoreContext(windowUiContext, storeContext);
    }

    public async Task<IReadOnlyList<StorePackageUpdate>> GetUpdates(CancellationToken cancellationToken)
    {
        var updatesTask = await _uiContext.RunOnUiThread(() =>
                _storeContext.GetAppAndOptionalStorePackageUpdatesAsync().AsTask(cancellationToken))
            .WaitAsync(cancellationToken).Vhc();
        var updates = await updatesTask.Vhc();
        return updates.ToArray();
    }

    public async Task<StorePackageUpdateResult> RequestDownloadAndInstall(IReadOnlyList<StorePackageUpdate> updates,
        CancellationToken cancellationToken)
    {
        var resultTask = await _uiContext.RunOnUiThread(() =>
                _storeContext.RequestDownloadAndInstallStorePackageUpdatesAsync(updates).AsTask(cancellationToken))
            .WaitAsync(cancellationToken).Vhc();
        return await resultTask.Vhc();
    }

    public async Task<StoreRateAndReviewResult> RequestRateAndReview(CancellationToken cancellationToken)
    {
        var resultTask = await _uiContext.RunOnUiThread(() =>
                _storeContext.RequestRateAndReviewAppAsync().AsTask(cancellationToken))
            .WaitAsync(cancellationToken).Vhc();
        return await resultTask.Vhc();
    }
}
