using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using Windows.Services.Store;

namespace VpnHood.AppLib.Stores.MicrosoftStore;

// The Microsoft Store counterpart of GooglePlayInAppUserReviewProvider: the Store's own rating and review dialog
// over the app's window, asked for after the app's own rating dialog (UserReviewDialog) got a top rating, as on
// Android and iOS. The Store tells how the dialog closed: the person's cancel is an answer, anything else an error,
// such as a package the Store does not know - one sideloaded, not installed from it (0x803F6107).
public class MicrosoftStoreInAppUserReviewProvider : IAppUserReviewProvider
{
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
