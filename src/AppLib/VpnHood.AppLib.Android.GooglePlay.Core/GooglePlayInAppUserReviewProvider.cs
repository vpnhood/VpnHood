using Android.Gms.Extensions;
using Google.Android.Play.Core.Review;
using Google.Android.Play.Core.Review.Testing;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Devices.Droid;
using VpnHood.Core.Client.Devices.Droid.Utils;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.Droid.GooglePlay;

public class GooglePlayInAppUserReviewProvider(bool testMode = false) : IAppUserReviewProvider
{
    public async Task RequestReview(IUiContext uiContext, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug("Requesting Google Play user review. TestMode: {testMode}", testMode);

        var appUiContext = (AndroidUiContext)uiContext;
        using var reviewManager = testMode
            ? new FakeReviewManager(appUiContext.Activity)
            : ReviewManagerFactory.Create(appUiContext.Activity);

        using var reviewInfo = await reviewManager.RequestReviewFlow()
            .AsAsync<ReviewInfo>()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        // Launch presents the Play review dialog, so start it on the UI thread (this method is called
        // from a web-server request thread) and await the Play task here — same pattern as
        // GooglePlayAppUpdaterProvider's update flow.
        // ReSharper disable AccessToDisposedClosure
        var launchReviewPlayTask = await AndroidUtils.RunOnUiThread(appUiContext.Activity, () =>
            reviewManager.LaunchReviewFlow(appUiContext.Activity, reviewInfo)).ConfigureAwait(false);
        // ReSharper restore AccessToDisposedClosure

        await launchReviewPlayTask
            .AsAsync()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}