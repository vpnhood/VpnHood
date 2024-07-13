using Android.Content;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Droid;
using VpnHood.Common.Utils;
using Xamarin.Google.Android.Play.Core.Review;
using Xamarin.Google.Android.Play.Core.Review.Testing;

namespace VpnHood.Client.App.Droid.GooglePlay;

public class GooglePlayInAppReviewService
{
    public static GooglePlayInAppReviewService Create()
    {
        var ret = new GooglePlayInAppReviewService();
        return ret;
    }
    
    public async Task InitializeReview(Context context, IUiContext uiContext)
    {
        try
        {
            var appUiContext = (AndroidUiContext)uiContext;
            //var reviewManager = ReviewManagerFactory.Create(context);
            var reviewManager = new FakeReviewManager(context);
            var reviewInfo = await GooglePlayTaskCompleteListener<ReviewInfo>.Create(reviewManager.RequestReviewFlow()).VhConfigureAwait();
            await GooglePlayTaskCompleteListener<object>.Create(reviewManager.LaunchReviewFlow(appUiContext.Activity, reviewInfo)).VhConfigureAwait();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        
    }
}