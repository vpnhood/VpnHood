using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Proxies;
using VpnHood.AppLib.Api.Sessions;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

public partial class ProtocolsView : UserControl, IPage
{
    private readonly MainView _host;
    private readonly List<(ChannelProtocol Protocol, OptionRow Row)> _rows = [];

    public ProtocolsView(MainView host)
    {
        _host = host;
        InitializeComponent();
        var s = Strings.Current;

        TcpForwardingItem.Title = s.CloakMode;
        TcpForwardingItem.Description = s.CloakModeShortDesc;
        DropQuicItem.Title = s.ProtocolBlockQuic;
        DropQuicItem.Description = s.ProtocolBlockQuicDesc;
        DropUdpItem.Title = s.ProtocolBlockUdp;
        DropUdpItem.Description = s.ProtocolBlockUdpDesc;

        // TCP the default, each shown when the build offers it
        AddRow(ChannelProtocol.Tcp, s.ProtocolTcpDesc, isDefault: true);
        AddRow(ChannelProtocol.Udp, s.ProtocolUdpDesc, isDefault: false);
        AddRow(ChannelProtocol.Quic, s.ProtocolQuicDesc, isDefault: false);
        Show();
    }

    private void AddRow(ChannelProtocol protocol, string description, bool isDefault)
    {
        if (!VhApp.IsShowProtocol(protocol))
            return;
        var row = new OptionRow { Title = AppText.ProtocolTitle(protocol), Description = description };
        if (isDefault)
            row.ChipLabel = Strings.Current.Default;
        row.Clicked += (_, _) => Choose(protocol);
        _rows.Add((protocol, row));
        ProtocolRows.Children.Add(row);
    }

    private void Show()
    {
        var state = VhApp.State;
        var settings = VhApp.UserSettings;
        var reason = state.TcpForwardingLockReason;

        TcpForwardingItem.IsOn = VhApp.IsTcpForwardingSwitchOn(state);
        TcpForwardingItem.IsDisabled = reason != TcpForwardingLockReason.None;
        ServerEnforcedAlert.IsVisible = reason is TcpForwardingLockReason.ServerRequiredOff or TcpForwardingLockReason.ServerRequiredOn;
        DomainEnforcedAlert.IsVisible = reason == TcpForwardingLockReason.SplitDomainRequiredOn;
        QuicPanel.IsVisible = TcpForwardingItem.IsOn;

        // a server that does not carry UDP or QUIC enforces its block; else the setting, as for TCP forwarding
        var isUdpPacketSupported = state.SessionInfo?.IsUdpPacketSupported ?? true;
        var isUdpBlocked = !isUdpPacketSupported || settings.DropUdp;
        DropUdpItem.IsOn = isUdpBlocked;
        DropUdpItem.IsDisabled = !isUdpPacketSupported;
        DropUdpEnforcedAlert.IsVisible = !isUdpPacketSupported;

        // QUIC runs over UDP, so a block on UDP holds its switch on; the setting is left as it is, for when it lifts
        var isQuicPacketSupported = state.SessionInfo?.IsQuicPacketSupported ?? true;
        DropQuicItem.IsOn = isUdpBlocked || !isQuicPacketSupported || settings.DropQuic;
        DropQuicItem.IsDisabled = isUdpBlocked || !isQuicPacketSupported;
        DropQuicEnforcedAlert.IsVisible = !isQuicPacketSupported && isUdpPacketSupported;

        var active = VhApp.ActiveProtocol(state);
        foreach (var (protocol, row) in _rows) {
            var isEnabled = VhApp.IsProtocolEnabled(state, protocol);
            row.IsChecked = protocol == active;
            row.IsDisabled = !isEnabled;
            if (!isEnabled)
                row.SetChip(Strings.Current.NotSupportedByServer, "on-note");
            else if (protocol == ChannelProtocol.Tcp)
                row.ChipLabel = Strings.Current.Default;
            else
                row.ChipLabel = null;
        }
    }

    public void FocusDefault()
    {
        if (!TcpForwardingItem.IsDisabled) {
            TcpForwardingItem.FindFirstButton()?.LandFocus();
            return;
        }

        var chosen = _rows.FirstOrDefault(x => x.Row.IsChecked).Row ?? _rows.FirstOrDefault().Row;
        if (chosen != null) chosen.LandFocus();
        else Header.FocusBack();
    }

    private async void Choose(ChannelProtocol protocol)
    {
        try {
            var settings = VhApp.UserSettings;
            settings.ChannelProtocol = protocol;
            await VhApp.SaveUserSettings(settings, CancellationToken.None);
            Show();
            _host.ViewModel.Refresh();
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
    }

    // the first time TCP forwarding is turned on, its page explains it
    private async void OnTcpForwardingToggled(object? sender, EventArgs e)
    {
        try {
            var settings = VhApp.UserSettings;
            if (!settings.IsTcpForwardingPrompted)
                _host.Navigate(FeaturePages.CloakMode(_host, isPrompt: true));

            settings.UseTcpForwarding = TcpForwardingItem.IsOn;
            await VhApp.SaveUserSettings(settings, CancellationToken.None);
            Show();
            _host.ViewModel.Refresh();
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
    }

    private async void OnDropQuicToggled(object? sender, EventArgs e)
    {
        try {
            var settings = VhApp.UserSettings;
            settings.DropQuic = DropQuicItem.IsOn;
            await VhApp.SaveUserSettings(settings, CancellationToken.None);
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
    }

    private async void OnDropUdpToggled(object? sender, EventArgs e)
    {
        try {
            var settings = VhApp.UserSettings;
            settings.DropUdp = DropUdpItem.IsOn;
            await VhApp.SaveUserSettings(settings, CancellationToken.None);
            Show();
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
    }

    private void OnLearnMoreClick(object? sender, RoutedEventArgs e)
    {
        _host.Navigate(FeaturePages.CloakMode(_host, isPrompt: false));
    }
}
