using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;
using VpnHood.AppUi.Presentation.Classic.Avalonia.ViewModels;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views.Dialogs;

public partial class BadgeDialog : DialogBase
{
    private readonly MainView _host;

    public BadgeDialog(MainView host, IReadOnlyList<FeatureBadge> badges)
    {
        _host = host;
        InitializeComponent();
        List.ItemsSource = badges;
    }

    public override void FocusDefault()
    {
        CloseButton.LandFocus();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    // the feature's page - or, on a TV, the pairing page with a hint: those pages are the phone's
    // work there, and the badge itself stays to say why traffic behaves as it does
    private void OnFeatureClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not FeatureBadge badge)
            return;
        Close();

        if (VhApp.IsTvUi) {
            _host.Navigate(new PairingView(_host, Strings.Current.RemoteAccessHintSettings));
            return;
        }

        IPage page = badge.Page switch {
            FeaturePage.SplitTunneling => new SplitTunnelingView(_host),
            FeaturePage.Servers => new LocationsView(_host.ViewModel, _host),
            FeaturePage.Dns => DnsView.Create(_host),
            _ => new ProxiesView(_host)
        };
        _host.Replace(page);
    }
}
