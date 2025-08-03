using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Droid.GooglePlay.Utils;
using VpnHood.Core.Client.Device.Droid;
using VpnHood.Core.Client.Device.UiContexts;
using Xamarin.Google.Android.Play.Core.Review;
using Xamarin.Google.Android.Play.Core.Review.Testing;

namespace VpnHood.AppLib.Droid.GooglePlay;

public class GooglePlayInAppReviewProvider(bool testMode = false) : IAppReviewProvider
{
    public async Task RequestReview(IUiContext uiContext)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        using var reviewManager = testMode
            ? ReviewManagerFactory.Create(appUiContext.Activity)
            : new FakeReviewManager(appUiContext.Activity);
        using var reviewInfo = await reviewManager.RequestReviewFlow().AsTask<ReviewInfo>().ConfigureAwait(false);
        await reviewManager.LaunchReviewFlow(appUiContext.Activity, reviewInfo!).AsTask().ConfigureAwait(false);
    }
}