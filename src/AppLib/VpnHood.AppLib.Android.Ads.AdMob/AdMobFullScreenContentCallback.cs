using Google.Android.Gms.Ads;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Abstractions.AdExceptions;

namespace VpnHood.AppLib.Droid.Ads.VhAdMob;

internal class AdMobFullScreenContentCallback : FullScreenContentCallback
{
    private bool _isClicked;
    private readonly TaskCompletionSource<ShowAdResult> _dismissedCompletionSource = new();
    public Task<ShowAdResult> DismissedTask => _dismissedCompletionSource.Task;

    public override void OnAdClicked()
    {
        _isClicked = true;
        base.OnAdClicked();
    }

    public override void OnAdDismissedFullScreenContent()
    {
        _dismissedCompletionSource.TrySetResult(_isClicked ? ShowAdResult.Clicked : ShowAdResult.Closed);
    }

    public override void OnAdFailedToShowFullScreenContent(AdError adError)
    {
        var message = string.IsNullOrWhiteSpace(adError.Message) ? "AdMob fullscreen empty message." : adError.Message;
        _dismissedCompletionSource.TrySetException(new ShowAdException(message));
    }
}