using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Resources;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Sessions;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

public partial class PromoteView : UserControl, IPage
{
    private readonly MainView _host;
    private readonly Guid _clientProfileId;
    private readonly string _serverLocation;
    private readonly bool _isPremiumLocation;

    public PromoteView(MainView host, Guid clientProfileId, string serverLocation, bool isPremiumLocation)
    {
        _host = host;
        _clientProfileId = clientProfileId;
        _serverLocation = serverLocation;
        _isPremiumLocation = isPremiumLocation;
        InitializeComponent();

        var s = Strings.Current;
        BackButton.IsVisible = !VhApp.IsTvUi;
        RichText.Apply(TitleText, isPremiumLocation ? s.SelectedLocationIsPremium : s.SelectedLocationIsFree);

        // the location's options, read again so the page is driven by current server data
        var info = VhApp.FindClientProfileInfo(clientProfileId);
        var options = info?.LocationInfos.FirstOrDefault(x => x.ServerLocation == serverLocation)?.Options;

        // the picture of the case, and over it the operator's own promotion when the app holds one
        AppImage.SetSource(PromoImage, AppAssets.ImagePath(isPremiumLocation ? "premium-servers.webp" : "free-to-premium-servers.webp"));
        if (VhApp.State.PromotionExists)
            _ = LoadPromotionImage();

        var isFree = !isPremiumLocation && options?.Normal != null;
        var isFreeByAd = !isPremiumLocation && options?.NormalByRewardedAd != null;
        FreeRow.IsVisible = isFree;
        FreeDesc.Text = options?.Normal == 0 ? s.SelectedFreeServerDesc : s.SelectedFreeServerUnlimitedDesc(options?.Normal ?? 0);
        FreeAdRow.IsVisible = isFreeByAd;
        FreeAdDesc.Text = options?.NormalByRewardedAd == 0
            ? s.SelectedFreeServerByRewardedAdUnlimitedDesc
            : s.SelectedFreeServerByRewardedAdDesc(options?.NormalByRewardedAd ?? 0);
        OrRow.IsVisible = isFree;

        if (options?.PremiumByRewardedAd is { } adMinutes)
            PremiumRows.Children.Add(new PromoteRow(Mdi.PlayBoxLockOpenOutline, s.WatchRewardedAd, s.WatchRewardedAdDesc(adMinutes), s.Connect,
                () => ConnectWith(ConnectPlanId.PremiumByRewardedAd)));
        if (options?.PremiumByTrial is { } trialMinutes)
            PremiumRows.Children.Add(new PromoteRow(Mdi.TimerLockOpenOutline, s.TryPremium, s.TryPremiumDesc(trialMinutes), s.Connect,
                () => ConnectWith(ConnectPlanId.PremiumByTrial)));
        if (options?.PremiumByPurchase == true || options?.PremiumByCode == true)
            PremiumRows.Children.Add(new PromoteRow(Mdi.CrownCircleOutline, s.GoPremium, s.GoPremiumDesc, s.Upgrade, () => {
                _host.Navigate(new PurchaseSubscriptionView(_host, clientProfileId));
                return Task.CompletedTask;
            }));
    }

    // The promotion comes through the API, as everything does; the picture of the case shows
    // until it arrives, and stays if it never does.
    private async Task LoadPromotionImage()
    {
        try {
            var bytes = await VhApp.Api.App.PromotionImage(CancellationToken.None);
            // the case's picture, if still on its way, must not land over the promotion
            AppImage.SetSource(PromoImage, null);
            PromoImage.Source = new Bitmap(new MemoryStream(bytes));
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not load the promotion image.");
        }
    }

    public void FocusDefault()
    {
        if (FreeRow.IsVisible) FreeRow.LandFocus();
        else if (FreeAdRow.IsVisible) FreeAdRow.LandFocus();
        else if (PremiumRows.Children.FirstOrDefault() is PromoteRow row) row.LandFocus();
        else if (BackButton.IsVisible) BackButton.LandFocus();
    }

    private async Task ConnectWith(ConnectPlanId planId)
    {
        await _host.ViewModel.ConnectWith(new ConnectRequest(_clientProfileId, _serverLocation, _isPremiumLocation, planId));
    }

    private async void OnFreeClick(object? sender, RoutedEventArgs e)
    {
        try {
            await ConnectWith(ConnectPlanId.Normal);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnFreeAdClick(object? sender, RoutedEventArgs e)
    {
        try {
            await ConnectWith(ConnectPlanId.NormalByRewardedAd);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        _host.GoBack();
    }
}
