using Avalonia.Controls;
using Avalonia.Interactivity;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.AvaloniaUI;
using VpnHood.AppLib.ClassicAvaloniaUi.Controls;
using VpnHood.AppLib.ClassicAvaloniaUi.Helpers;
using VpnHood.AppLib.Contracts.App;
using VpnHood.AppLib.Contracts.Proxies;
using VpnHood.AppLib.Contracts.Sessions;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Views;

public partial class ProtocolsView : UserControl, IPage
{
    private readonly MainView _host;
    private readonly List<(ChannelProtocol Protocol, OptionRow Row)> _rows = [];

    public ProtocolsView(MainView host)
    {
        _host = host;
        InitializeComponent();
        var s = Strings.Current;

        CloakCard.IsVisible = AppModel.Features.IsTcpProxySupported;
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
        if (!AppModel.IsShowProtocol(protocol))
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
        var state = AppModel.State;
        var settings = AppModel.UserSettings;
        var reason = state.TcpProxyUsageReason;

        CloakItem.IsOn = state.SessionStatus?.IsTcpProxy ?? settings.UseTcpProxy;
        CloakItem.IsDisabled = reason != TcpProxyUsageReason.None;
        ServerEnforcedAlert.IsVisible = reason is TcpProxyUsageReason.ServerRequiredOff or TcpProxyUsageReason.ServerRequiredOn;
        DomainEnforcedAlert.IsVisible = reason == TcpProxyUsageReason.SplitDomainRequiredOn;
        QuicPanel.IsVisible = CloakItem.IsOn;
        DropQuicItem.IsOn = state.SessionStatus?.IsDropQuic ?? settings.DropQuic;

        var active = AppModel.ActiveProtocol(state);
        foreach (var (protocol, row) in _rows) {
            var isEnabled = AppModel.IsProtocolEnabled(state, protocol);
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

    private async void Choose(ChannelProtocol protocol)
    {
        try {
            var settings = AppModel.UserSettings;
            settings.ChannelProtocol = protocol;
            await AppModel.SaveUserSettings(settings, CancellationToken.None);
            Show();
            _host.ViewModel.Refresh();
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
    }

    // the first time the cloak is turned on, its page explains it (protocols.vue's cloakMode setter)
    private async void OnCloakToggled(object? sender, EventArgs e)
    {
        try {
            var settings = AppModel.UserSettings;
            if (!settings.IsTcpProxyPrompted)
                _host.Navigate(FeaturePages.CloakMode(_host));

            settings.UseTcpProxy = CloakItem.IsOn;
            await AppModel.SaveUserSettings(settings, CancellationToken.None);
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
            var settings = AppModel.UserSettings;
            settings.DropQuic = DropQuicItem.IsOn;
            await AppModel.SaveUserSettings(settings, CancellationToken.None);
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
    }

    private void OnLearnMoreClick(object? sender, RoutedEventArgs e)
    {
        _host.Navigate(FeaturePages.CloakMode(_host));
    }
}
