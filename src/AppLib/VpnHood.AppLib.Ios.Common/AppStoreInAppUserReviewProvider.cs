using StoreKit;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Devices.Ios.Utils;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.Ios.Common;

// App Store counterpart of GooglePlayInAppUserReviewProvider: presents the native in-app rating
// dialog via SKStoreReviewController. The OS decides whether the dialog actually appears — it is
// throttled (at most a few prompts per year per app) and never shows for TestFlight/dev builds —
// and gives no completion signal, so this returns once the request is made, not when the user is
// done. That is the platform contract; the SPA's own rating dialog runs first (UserReviewDialog)
// and this native prompt is only requested after a top rating, matching the Android flow.
public class AppStoreInAppUserReviewProvider : IAppUserReviewProvider
{
    public async Task RequestReview(IUiContext uiContext, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug("Requesting App Store user review.");

        await IosUtils.RunOnUiThread(() => {
            // the review dialog is presented on a scene, so it needs the foreground-active one
            var scene = UIApplication.SharedApplication.ConnectedScenes
                            .OfType<UIWindowScene>()
                            .FirstOrDefault(x => x.ActivationState == UISceneActivationState.ForegroundActive)
                        ?? throw new InvalidOperationException(
                            "Could not find a foreground-active scene to present the review dialog.");

            SKStoreReviewController.RequestReview(scene);
        }).Vhc();
    }
}
