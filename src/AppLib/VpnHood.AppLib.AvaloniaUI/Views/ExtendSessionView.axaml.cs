using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppLib.AvaloniaUI.Controls;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class ExtendSessionView : UserControl, IPage
{
    private readonly MainView _host;

    public ExtendSessionView(MainView host)
    {
        _host = host;
        InitializeComponent();

        var s = Strings.Current;
        BackButton.IsVisible = !AppData.IsTvUi;
        RichText.Apply(TitleText, s.ExtendPremiumSession);

        var state = AppData.State;
        var options = state.ClientProfile?.SelectedLocationInfo?.Options;
        if (state.SessionStatus?.CanExtendByRewardedAd == true)
            Rows.Children.Add(new PromoteRow(Mdi.PlayBoxLockOpenOutline, s.WatchRewardedAd,
                s.ExtendByRewardedAdDesc(options?.PremiumByRewardedAd ?? 0), s.ShowAd, ShowRewardedAd));
        if (options?.PremiumByPurchase == true || options?.PremiumByCode == true)
            Rows.Children.Add(new PromoteRow(Mdi.CrownCircleOutline, s.GoPremium, s.GoPremiumDesc, s.Upgrade, () => {
                _host.Navigate(new PurchaseSubscriptionView(_host, null));
                return Task.CompletedTask;
            }));
    }

    public void FocusDefault()
    {
        if (Rows.Children.FirstOrDefault() is PromoteRow row) row.LandFocus();
        else if (BackButton.IsVisible) BackButton.LandFocus();
    }

    // the ad, then the word that the session grew, back on the home
    private async Task ShowRewardedAd()
    {
        using (_host.Loading(Strings.Current.ExtendByRewardedAdNote))
            await AppData.Api.App.ExtendByRewardedAd(CancellationToken.None);
        _host.ShowSnackbar(Strings.Current.ExtendByRewardedAdConfirmMsg, SnackbarKind.Active);
        _host.GoHome();
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        _host.GoBack();
    }
}
