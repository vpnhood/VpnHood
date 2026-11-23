using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using VpnHood.AppUi.Common;
using VpnHood.AppHosting.Avalonia;
using VpnHood.AppUi.Avalonia.Classic.Controls;
using VpnHood.AppUi.Avalonia.Classic.Helpers;
using VpnHood.AppLogic.Api.App;
using VpnHood.AppLogic.Api.Proxies;
using VpnHood.AppLogic.Api.Sessions;

namespace VpnHood.AppUi.Avalonia.Classic.Views;

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

    // the locks follow the session while the page is open: a server's Cloak Mode, known only once it connects, frees
    // the blocks the page opened with locked
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        VhApp.StateReloaded += VhApp_StateReloaded;
        Show();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        VhApp.StateReloaded -= VhApp_StateReloaded;
        base.OnDetachedFromVisualTree(e);
    }

    private void VhApp_StateReloaded(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            Show();
        else
            Dispatcher.UIThread.Post(Show);
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

        // The blocks act only where TCP forwarding is chosen, by the switch or by the server's Cloak Mode. Where only the
        // server's adapter or the domain rules hold the switch on, they are locked off, with the switch's own reason
        var s = Strings.Current;
        var isBlockLockedOff = !settings.UseTcpForwarding && state.SessionInfo?.IsTcpForwardingRequired != true;
        var blockLockText = reason == TcpForwardingLockReason.SplitDomainRequiredOn
            ? s.EnforcedByDomainFilter
            : s.EnforcedByServer;

        // a server that does not carry UDP or QUIC enforces its block; else the setting, as for TCP forwarding
        var isUdpPacketSupported = state.SessionInfo?.IsUdpPacketSupported ?? true;
        var isUdpBlocked = !isUdpPacketSupported || (settings.DropUdp && !isBlockLockedOff);
        DropUdpItem.IsOn = isUdpBlocked;
        DropUdpItem.IsDisabled = !isUdpPacketSupported || isBlockLockedOff;
        DropUdpEnforcedAlert.IsVisible = !isUdpPacketSupported || isBlockLockedOff;
        DropUdpEnforcedText.Text = isUdpPacketSupported ? blockLockText : s.EnforcedByServer;

        // QUIC runs over UDP, so a block on UDP holds its switch on; the setting is left as it is, for when it lifts
        var isQuicPacketSupported = state.SessionInfo?.IsQuicPacketSupported ?? true;
        DropQuicItem.IsOn = isUdpBlocked || !isQuicPacketSupported || (settings.DropQuic && !isBlockLockedOff);
        DropQuicItem.IsDisabled = isUdpBlocked || !isQuicPacketSupported || isBlockLockedOff;
        DropQuicEnforcedAlert.IsVisible = (!isQuicPacketSupported && isUdpPacketSupported) || isBlockLockedOff;
        DropQuicEnforcedText.Text = isQuicPacketSupported ? blockLockText : s.EnforcedByServer;

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
