using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppLib.AvaloniaUI.Controls;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.AppLib.Dtos;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class ProtocolsView : UserControl, IPage
{
    private readonly MainView _host;
    private readonly VpnHoodApp _app = VpnHoodApp.Instance;
    private readonly List<(ChannelProtocol Protocol, OptionRow Row)> _rows = [];

    public ProtocolsView(MainView host)
    {
        _host = host;
        InitializeComponent();
        var s = Strings.Current;

        CloakCard.IsVisible = _app.Features.IsTcpProxySupported;
        CloakItem.Title = s.CloakMode;
        CloakItem.Description = s.CloakModeShortDesc;
        DropQuicItem.Title = s.ProtocolBlockQuic;
        DropQuicItem.Description = s.ProtocolBlockQuicDesc;

        // protocols.vue's protocolItems: TCP the default, each shown when the build offers it
        AddRow(ChannelProtocol.Tcp, s.ProtocolTcpDesc, isDefault: true);
        AddRow(ChannelProtocol.Udp, s.ProtocolUdpDesc, isDefault: false);
        AddRow(ChannelProtocol.Quic, s.ProtocolQuicDesc, isDefault: false);
        Show();
    }

    private void AddRow(ChannelProtocol protocol, string description, bool isDefault)
    {
        if (!AppData.IsShowProtocol(protocol))
            return;
        var row = new OptionRow { Title = AppData.ProtocolTitle(protocol), Description = description };
        if (isDefault)
            row.ChipLabel = Strings.Current.Default;
        row.Clicked += (_, _) => Choose(protocol);
        _rows.Add((protocol, row));
        ProtocolRows.Children.Add(row);
    }

    private void Show()
    {
        var state = _app.State;
        var settings = _app.UserSettings;
        var reason = state.TcpProxyUsageReason;

        CloakItem.IsOn = state.SessionStatus?.IsTcpProxy ?? settings.UseTcpProxy;
        CloakItem.IsDisabled = reason != TcpProxyUsageReason.None;
        ServerEnforcedAlert.IsVisible = reason is TcpProxyUsageReason.ServerRequiredOff or TcpProxyUsageReason.ServerRequiredOn;
        DomainEnforcedAlert.IsVisible = reason == TcpProxyUsageReason.SplitDomainRequiredOn;
        QuicPanel.IsVisible = CloakItem.IsOn;
        DropQuicItem.IsOn = state.SessionStatus?.IsDropQuic ?? settings.DropQuic;

        var active = AppData.ActiveProtocol(state);
        foreach (var (protocol, row) in _rows) {
            var isEnabled = AppData.IsProtocolEnabled(state, protocol);
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
        if (CloakCard.IsVisible && !CloakItem.IsDisabled) {
            CloakItem.FindFirstButton()?.LandFocus();
            return;
        }

        var chosen = _rows.FirstOrDefault(x => x.Row.IsChecked).Row ?? _rows.FirstOrDefault().Row;
        if (chosen != null) chosen.LandFocus();
        else Header.FocusBack();
    }

    private void Choose(ChannelProtocol protocol)
    {
        _app.UserSettings.ChannelProtocol = protocol;
        _app.SettingsService.Save();
        Show();
        _host.ViewModel.Refresh();
    }

    // the first time the cloak is turned on, its page explains it (protocols.vue's cloakMode setter)
    private void OnCloakToggled(object? sender, EventArgs e)
    {
        if (!_app.UserSettings.IsTcpProxyPrompted)
            _host.Navigate(FeaturePages.CloakMode(_host));

        _app.UserSettings.UseTcpProxy = CloakItem.IsOn;
        _app.SettingsService.Save();
        Show();
        _host.ViewModel.Refresh();
    }

    private void OnDropQuicToggled(object? sender, EventArgs e)
    {
        _app.UserSettings.DropQuic = DropQuicItem.IsOn;
        _app.SettingsService.Save();
    }

    private void OnLearnMoreClick(object? sender, RoutedEventArgs e)
    {
        _host.Navigate(FeaturePages.CloakMode(_host));
    }
}
