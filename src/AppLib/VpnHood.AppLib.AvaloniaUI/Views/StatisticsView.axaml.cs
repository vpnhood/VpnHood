using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class StatisticsView : UserControl, IPage
{
    private readonly VpnHoodApp _app = VpnHoodApp.Instance;

    public StatisticsView(MainView host)
    {
        _ = host;
        InitializeComponent();
        Fill();
    }

    private void Fill()
    {
        var s = Strings.Current;
        var state = _app.State;
        var session = state.SessionInfo;
        var status = state.SessionStatus;
        if (session == null)
            return;

        var access = session.AccessInfo;
        var isPremiumUser = AppData.IsPremiumUser;
        var isConnected = AppData.IsConnected(state);
        // no traffic figures for a person in China (statistics.vue's isChinaCountry)
        var isChina = string.Equals(state.ClientCountryInfo?.CountryCode, "CN", StringComparison.OrdinalIgnoreCase);

        if (isPremiumUser && access != null) {
            var card = AddCard(s.PremiumInfo, Mdi.Crown, s.StatisticsDateCardDesc);
            if (AppData.IsPremiumSupported)
                AddRow(card, s.PremiumBy, AppData.IsPremiumByAccount ? s.PurchaseSubscription : s.PremiumCode, "active");
            AddRow(card, s.ActivatedOn, Format.ShortDate(access.CreatedTime), "active");
            AddRow(card, s.ExpirationDate, access.ExpirationTime is { } expire ? Format.ShortDate(expire) : s.Never, access.ExpirationTime != null ? "error" : "active");
            AddRow(card, s.LastUsed, Format.ShortDate(access.LastUsedTime), "highlight", isLast: true);
        }

        if (access != null) {
            var card = AddCard(s.ServerAndIp, Mdi.ServerOutline, s.StatisticsServerCardDesc);
            if (isConnected) {
                var isUdp = AppData.IsProtocolEnabled(state, ChannelProtocol.Udp);
                AddRow(card, s.YourProtectedIp, session.ClientPublicIpAddress.ToString(), "highlight");
                AddRow(card, s.Country, session.ServerLocationInfo?.TranslatedCountryName ?? "", "active");
                AddRow(card, s.Region, session.ServerLocationInfo?.RegionName ?? "", "active");
                AddRow(card, s.UdpSupported, isUdp ? s.Yes : s.No, isUdp ? "active" : "error", isLast: true);
            }
            else {
                AddNotice(card, s.DisplayInfoAfterConnection);
            }
            card.Opacity = isConnected ? 1 : 0.6;
        }

        if (isPremiumUser && access != null) {
            var card = AddCard(s.Devices, Mdi.CellphoneLink, s.StatisticsDevicesCardDesc(access.DeviceLifeSpan));
            var summary = access.DevicesSummary;
            var used = summary?.HasMoreDevices == true ? s.MoreThanXDevices(summary.DeviceCount) : (summary?.DeviceCount ?? 0).ToString();
            AddRow(card, s.UsedDevice, used, "highlight");
            AddRow(card, s.MaxDevice, access.MaxDeviceCount > 0 ? access.MaxDeviceCount.ToString() : s.Unlimited, "active", isLast: true);
        }

        if (!isChina) {
            var card = AddCard(s.SessionTraffic, Mdi.ChartTimelineVariant, s.StatisticsSessionTrafficCardDesc);
            AddRow(card, s.Used, Format.Traffic(status?.SessionTraffic.Total ?? 0), "highlight", isLtr: true);
            var max = status?.SessionMaxTraffic ?? 0;
            AddRow(card, s.MaxTraffic, max > 0 ? Format.Traffic(max) : s.Unlimited, max > 0 ? "error" : "active", isLast: true, isLtr: true);
        }

        if (!isChina) {
            var card = AddCard(s.MonthlyTraffic, Mdi.ChartTimelineVariant, s.StatisticsMonthlyTrafficCardDesc);
            AddRow(card, s.Used, Format.Traffic(status?.CycleTraffic.Total ?? 0), "highlight", isLtr: true);
            var max = access?.MaxCycleTraffic ?? 0;
            AddRow(card, s.MaxTraffic, max > 0 ? Format.Traffic(max) : s.Unlimited, max > 0 ? "error" : "active", isLast: true, isLtr: true);
        }

        if (isPremiumUser && !isChina) {
            var card = AddCard(s.TotalTraffic, Mdi.ChartTimelineVariant, s.StatisticsTotalTrafficCardDesc);
            AddRow(card, s.Used, Format.Traffic(status?.TotalTraffic.Total ?? 0), "highlight", isLtr: true);
            var max = access?.MaxTotalTraffic ?? 0;
            AddRow(card, s.MaxTraffic, max > 0 ? Format.Traffic(max) : s.Unlimited, max > 0 ? "error" : "active", isLast: true, isLtr: true);
        }
    }

    // a card: the title with its glyph, the subtitle, then the rows
    private StackPanel AddCard(string title, string glyph, string subtitle)
    {
        var body = new StackPanel { Spacing = 4 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var titleText = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        titleText.Classes.Add("card-title");
        var icon = new TextBlock { Text = glyph, FontSize = 18, VerticalAlignment = VerticalAlignment.Center };
        icon.Classes.Add("mdi");
        icon.Classes.Add("disabled");
        titleRow.Children.Add(titleText);
        titleRow.Children.Add(icon);
        var subtitleText = new TextBlock { Text = subtitle, Margin = new Thickness(0, 0, 0, 8) };
        subtitleText.Classes.Add("card-subtitle");
        body.Children.Add(titleRow);
        body.Children.Add(subtitleText);

        var card = new Border { Child = body, Padding = new Thickness(16, 12) };
        card.Classes.Add("config-card");
        Cards.Children.Add(card);
        return body;
    }

    // a label at the start, the value at the end in its colour, a hairline under all but the last;
    // a traffic figure is pinned left to right, as statistics.vue pins it (dir="ltr")
    private static void AddRow(StackPanel card, string label, string value, string valueClass, bool isLast = false, bool isLtr = false)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 5) };
        var labelText = new TextBlock { Text = label, Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
        labelText.Classes.Add("body-small");
        labelText.Classes.Add("disabled");
        var valueText = new TextBlock { Text = value, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        if (isLtr)
            valueText.FlowDirection = FlowDirection.LeftToRight;
        valueText.Classes.Add("body-small");
        valueText.Classes.Add(valueClass);
        Grid.SetColumn(valueText, 1);
        row.Children.Add(labelText);
        row.Children.Add(valueText);
        card.Children.Add(row);
        if (!isLast) {
            var divider = new Border();
            divider.Classes.Add("divider");
            card.Children.Add(divider);
        }
    }

    private static void AddNotice(StackPanel card, string text)
    {
        var panel = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8) };
        var icon = new TextBlock { Text = Mdi.InformationOutline, FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center };
        icon.Classes.Add("mdi");
        icon.Classes.Add("disabled");
        var message = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        message.Classes.Add("body-small");
        message.Classes.Add("disabled");
        panel.Children.Add(icon);
        panel.Children.Add(message);
        card.Children.Add(panel);
    }

    public void FocusDefault()
    {
        Header.FocusBack();
    }
}
