using VpnHood.Client.App.Droid.GooglePlay.Utils;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Droid;
using VpnHood.Common.Utils;
using Xamarin.Google.Android.Play.Core.Review;
using Xamarin.Google.Android.Play.Core.Review.Testing;
using Exception = System.Exception;

namespace VpnHood.Client.App.Droid.GooglePlay;

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
        try
        {
            var appUiContext = (AndroidUiContext)uiContext;
            //var reviewManager = ReviewManagerFactory.Create(appUiContext.Activity);
            using var reviewManager = new FakeReviewManager(appUiContext.Activity);
            using var reviewInfo = await reviewManager.RequestReviewFlow().AsTask<ReviewInfo>().VhConfigureAwait();
            await reviewManager.LaunchReviewFlow(appUiContext.Activity, reviewInfo).AsTask().VhConfigureAwait();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        
    }
}