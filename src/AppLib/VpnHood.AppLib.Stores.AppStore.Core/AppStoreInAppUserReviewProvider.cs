#if !TVOS // an Apple TV has no in-app review prompt: StoreKit has none for tvOS
using StoreKit;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Devices.Ios.Utils;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppLib.Stores.AppStore;

/// <summary>Provides the App Store's in-app rating dialog, through StoreKit's review controller.</summary>
/// <remarks>
/// iOS decides whether the dialog shows: a few times a year at most, and never in TestFlight, while a development build
/// always shows it, for testing. It tells nothing back, so a request completes once it is made, not when the person is
/// done.
/// </remarks>
public class AppStoreInAppUserReviewProvider : IAppUserReviewProvider
{
    /// <inheritdoc/>
    /// <remarks>It asks for the dialog on the app's foreground scene.</remarks>
    /// <exception cref="InvalidOperationException">No scene of the app is in front.</exception>
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
#endif
