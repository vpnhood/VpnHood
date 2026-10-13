using Avalonia.Android;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Ads;
using VpnHood.AppLib.Abstractions.Ads.AdExceptions;
using VpnHood.AppLib.App.Android.Activities;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Views;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Client.Devices.Android;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.App.Connect.Android.Google;

// The internal ad as CONNECT's film, played full screen by AssetVideoActivity the way a network shows
// its interstitial: the fallback when no network has an ad, where the remote settings turn it on. The
// build packs the film only when the Connect repo sits beside this one (the csproj). Once the film
// has run, Close ends it and Go premium opens the purchase page.
internal sealed class InternalVideoAdProvider : IAdProvider
{
    private const string AssetFolder = "ads";
    private const string AssetName = "internal-ad.mp4";

    // the film's own background: the film is nearly square, so this fills the rest of a phone's
    // screen and the space around it reads as the film's; a new film may need a new colour
    private static readonly System.Drawing.Color FilmBackground = System.Drawing.Color.FromArgb(0x1A, 0x16, 0x34);

    public string NetworkName => "InternalAd";
    public AdType AdType => AdType.InterstitialAd;
    public DateTime? AdLoadedTime { get; private set; }
    public TimeSpan AdLifeSpan => TimeSpan.FromMinutes(5);

    public static bool IsPacked()
    {
        return global::Android.App.Application.Context.Assets?.List(AssetFolder)?.Contains(AssetName) == true;
    }

    // nothing to fetch: the film is in the package
    public Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        AdLoadedTime = FastDateTime.UtcNow;
        return Task.CompletedTask;
    }

    // gone before its end, the film was not shown: a caller that needs an ad must know it got none
    public async Task<ShowAdResult> ShowAd(IUiContext uiContext, string? customData,
        CancellationToken cancellationToken)
    {
        var activityEvent = ((AndroidUiContext)uiContext).ActivityEvent;
        if (activityEvent.Activity.IsDestroyed)
            throw new ShowAdException("MainActivity has been destroyed before showing the ad.");

        try {
            var result = await AssetVideoActivity.PlayAsync(activityEvent, new AssetVideoParams {
                AssetPath = $"{AssetFolder}/{AssetName}",
                BackgroundColor = FilmBackground,
                CloseText = Strings.Current.Close,
                ActionText = Strings.Current.GoPremium,
                CanCloseEarly = AppConstants.IsDebugMode
            }, cancellationToken).ConfigureAwait(false);

            switch (result) {
                case AssetVideoResult.Closed:
                    return ShowAdResult.Closed;

                case AssetVideoResult.Action:
                    ShowPurchasePage();
                    return ShowAdResult.Clicked;

                default:
                    throw new ShowAdException("The internal ad was gone before its end.");
            }
        }
        finally {
            AdLoadedTime = null;
        }
    }

    // the purchase page, in the UI the film covered, as the home's own Go premium opens it
    private static void ShowPurchasePage()
    {
        Dispatcher.UIThread.Post(() => {
            if (AppUiContext.Context is AndroidUiContext { Activity: AvaloniaActivity { Content: MainView host } })
                host.Navigate(new PurchaseSubscriptionView(host, null));
            else
                VhLogger.Instance.LogWarning("The purchase page could not be opened: the app shows no main view.");
        });
    }

    public void Dispose()
    {
    }
}
