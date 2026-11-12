using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using Windows.Services.Store;

namespace VpnHood.AppLib.Stores.MicrosoftStore;

/// <summary>Provides the Microsoft Store's own rating and review dialog, over the app's desktop window.</summary>
/// <remarks>
/// The Store tells how the dialog closed: a success and the person's cancel complete normally, and any other status
/// throws, as for an app the Store does not associate with its listing (0x803F6107).
/// </remarks>
public class MicrosoftStoreInAppUserReviewProvider : IAppUserReviewProvider
{
    /// <inheritdoc/>
    /// <remarks>It completes once the dialog has closed.</remarks>
    /// <exception cref="InvalidOperationException">
    /// The window has no native handle, or the Store could not show its dialog.
    /// </exception>
    public async Task RequestReview(IUiContext uiContext, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug("Requesting Microsoft Store rating and review.");
        var storeContext = await MicrosoftStoreContext.Create(uiContext).Vhc();
        var result = await storeContext.RequestRateAndReview(cancellationToken).Vhc();
        if (result.Status is not (StoreRateAndReviewStatus.Succeeded or StoreRateAndReviewStatus.CanceledByUser))
            throw new InvalidOperationException(
                $"The Microsoft Store could not show its review dialog. Status: {result.Status}", result.ExtendedError);

        VhLogger.Instance.LogInformation("Microsoft Store review dialog closed. Status: {Status}, WasUpdated: {WasUpdated}",
            result.Status, result.WasUpdated);
    }
}
