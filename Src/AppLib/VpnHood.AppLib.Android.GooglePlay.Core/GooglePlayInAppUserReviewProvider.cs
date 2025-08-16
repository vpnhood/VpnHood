using Android.Gms.Extensions;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device.Droid;
using VpnHood.Core.Client.Device.UiContexts;
using Xamarin.Google.Android.Play.Core.Review;
using Xamarin.Google.Android.Play.Core.Review.Testing;

namespace VpnHood.AppLib.Droid.GooglePlay;

public class GooglePlayInAppUserReviewProvider(bool testMode = false) : IAppUserReviewProvider
{
    public async Task RequestReview(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        using var reviewManager = testMode
            ? new FakeReviewManager(appUiContext.Activity)
            : ReviewManagerFactory.Create(appUiContext.Activity);
        
        using var reviewInfo = await reviewManager.RequestReviewFlow()
            .AsAsync<ReviewInfo>()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        await reviewManager.LaunchReviewFlow(appUiContext.Activity, reviewInfo!)
            .AsAsync()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}