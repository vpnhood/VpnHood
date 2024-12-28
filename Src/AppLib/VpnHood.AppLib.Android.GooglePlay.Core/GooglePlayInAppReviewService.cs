using VpnHood.AppLib.Droid.GooglePlay.Utils;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Client.Device.Droid;
using VpnHood.Core.Common.Utils;
using Xamarin.Google.Android.Play.Core.Review;
using Xamarin.Google.Android.Play.Core.Review.Testing;

namespace VpnHood.AppLib.Droid.GooglePlay;

public class GooglePlayInAppReviewService
{
    private GooglePlayInAppReviewService()
    {
    }

    public static GooglePlayInAppReviewService Create()
    {
        var ret = new GooglePlayInAppReviewService();
        return ret;
    }

    public async Task InitializeReview(IUiContext uiContext)
    {
        try {
            var appUiContext = (AndroidUiContext)uiContext;
            //var reviewManager = ReviewManagerFactory.Create(appUiContext.Activity);
            using var reviewManager = new FakeReviewManager(appUiContext.Activity);
            using var reviewInfo = await reviewManager.RequestReviewFlow().AsTask<ReviewInfo>().ConfigureAwait(false);
            await reviewManager.LaunchReviewFlow(appUiContext.Activity, reviewInfo!).AsTask().ConfigureAwait(false);
        }
        catch (Exception e) {
            Console.WriteLine(e);
            throw;
        }
    }
}