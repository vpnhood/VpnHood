using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using VpnHood.AppLib.AvaloniaUI.Controls;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.Core.Common.Tokens;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class PromoteView : UserControl, IPage
{
    private readonly MainView _host;
    private readonly VpnHoodApp _app = VpnHoodApp.Instance;
    private readonly Guid _clientProfileId;
    private readonly string _serverLocation;
    private readonly bool _isPremiumLocation;
    private readonly ServerLocationOptions? _options;

    public PromoteView(MainView host, Guid clientProfileId, string serverLocation, bool isPremiumLocation)
    {
        _host = host;
        _clientProfileId = clientProfileId;
        _serverLocation = serverLocation;
        _isPremiumLocation = isPremiumLocation;
        InitializeComponent();

        var s = Strings.Current;
        BackButton.IsVisible = !AppData.IsTvUi;
        RichText.Apply(TitleText, isPremiumLocation ? s.SelectedLocationIsPremium : s.SelectedLocationIsFree);

        // the location's options, read again so the page is driven by current server data
        var info = _app.ClientProfileService.FindInfo(clientProfileId);
        _options = info?.LocationInfos.FirstOrDefault(x => x.ServerLocation == serverLocation)?.Options;

        // the operator's own promotion when the app holds one, else the picture of the case
        var promotion = _app.SettingsService.PromotionImageFilePath;
        PromoImage.Source = _app.State.PromotionExists && promotion != null && File.Exists(promotion)
            ? new Bitmap(promotion)
            : AppAssets.Image(isPremiumLocation ? "premium-servers.webp" : "free-to-premium-servers.webp");

        var isFree = !isPremiumLocation && _options?.Normal != null;
        var isFreeByAd = !isPremiumLocation && _options?.NormalByRewardedAd != null;
        FreeRow.IsVisible = isFree;
        FreeDesc.Text = _options?.Normal == 0 ? s.SelectedFreeServerDesc : s.SelectedFreeServerUnlimitedDesc(_options?.Normal ?? 0);
        FreeAdRow.IsVisible = isFreeByAd;
        FreeAdDesc.Text = _options?.NormalByRewardedAd == 0
            ? s.SelectedFreeServerByRewardedAdUnlimitedDesc
            : s.SelectedFreeServerByRewardedAdDesc(_options?.NormalByRewardedAd ?? 0);
        OrRow.IsVisible = isFree;

        if (_options?.PremiumByRewardedAd is { } adMinutes)
            PremiumRows.Children.Add(new PromoteRow(Mdi.PlayBoxLockOpenOutline, s.WatchRewardedAd, s.WatchRewardedAdDesc(adMinutes), s.Connect,
                () => ConnectWith(ConnectPlanId.PremiumByRewardedAd)));
        if (_options?.PremiumByTrial is { } trialMinutes)
            PremiumRows.Children.Add(new PromoteRow(Mdi.TimerLockOpenOutline, s.TryPremium, s.TryPremiumDesc(trialMinutes), s.Connect,
                () => ConnectWith(ConnectPlanId.PremiumByTrial)));
        if (_options?.PremiumByPurchase == true || _options?.PremiumByCode == true)
            PremiumRows.Children.Add(new PromoteRow(Mdi.CrownCircleOutline, s.GoPremium, s.GoPremiumDesc, s.Upgrade, () => {
                _host.Navigate(new PurchaseSubscriptionView(_host, clientProfileId));
                return Task.CompletedTask;
            }));
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
        await ConnectWith(ConnectPlanId.Normal);
    }

    private async void OnFreeAdClick(object? sender, RoutedEventArgs e)
    {
        await ConnectWith(ConnectPlanId.NormalByRewardedAd);
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        _host.GoBack();
    }
}
