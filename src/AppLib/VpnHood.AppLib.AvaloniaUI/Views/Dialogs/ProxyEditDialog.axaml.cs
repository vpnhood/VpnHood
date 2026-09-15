using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.AppLib.Services.Proxies;
using VpnHood.Core.Proxies.Management.Abstractions;

namespace VpnHood.AppLib.AvaloniaUI.Views.Dialogs;

public partial class ProxyEditDialog : DialogBase
{
    private readonly MainView _host;
    private readonly ProxySheetKind _kind;
    private readonly string? _oldId;
    private readonly AppProxyEndPointService _service = VpnHoodApp.Instance.Services.ProxyEndPointService;
    private ProxyProtocol _protocol;
    private bool _isProcessing;

    // Add: empty fields; AddList: the import box; Edit: the saved proxy's fields and record
    public ProxyEditDialog(MainView host, ProxySheetKind kind, AppProxyEndPointInfo? proxy)
    {
        _host = host;
        _kind = kind;
        _oldId = proxy?.EndPoint.Id;
        InitializeComponent();
        var s = Strings.Current;

        TitleText.Text = kind switch {
            ProxySheetKind.Add => s.AddProxy,
            ProxySheetKind.AddList => s.AddProxyList,
            _ => s.EditProxy
        };
        ListPanel.IsVisible = kind == ProxySheetKind.AddList;
        FieldsPanel.IsVisible = kind != ProxySheetKind.AddList;
        StatusPanel.IsVisible = kind == ProxySheetKind.Edit;
        RemoveButton.IsVisible = kind == ProxySheetKind.Edit;
        SaveButton.Content = kind == ProxySheetKind.Edit ? s.Save : s.Add;
        ListBox.PlaceholderText = s.ProxyImportLabel;
        HostBox.PlaceholderText = s.ProxyHost;
        PortBox.PlaceholderText = s.ProxyPort;
        UsernameBox.PlaceholderText = s.ProxyUsername;
        PasswordBox.PlaceholderText = s.ProxyPassword;

        foreach (var protocol in new[] { ProxyProtocol.Http, ProxyProtocol.Https, ProxyProtocol.Socks4, ProxyProtocol.Socks5 }) {
            var item = new Button { Content = protocol.ToString().ToLowerInvariant() };
            item.Classes.Add("menu-item");
            item.Click += (_, _) => {
                _protocol = protocol;
                ProtocolText.Text = protocol.ToString().ToLowerInvariant();
                ProtocolButton.Flyout?.Hide();
            };
            ProtocolMenu.Children.Add(item);
        }

        var endPoint = proxy?.EndPoint;
        EnabledSwitch.IsChecked = endPoint?.IsEnabled ?? true;
        HostBox.Text = endPoint?.Host ?? "";
        PortBox.Text = (endPoint?.Port ?? 8080).ToString();
        _protocol = endPoint?.Protocol ?? ProxyProtocol.Http;
        ProtocolText.Text = _protocol.ToString().ToLowerInvariant();
        UsernameBox.Text = endPoint?.Username ?? "";
        PasswordBox.Text = endPoint?.Password ?? "";
        var hasAuth = !string.IsNullOrEmpty(endPoint?.Username) || !string.IsNullOrEmpty(endPoint?.Password);
        AuthSwitch.IsChecked = hasAuth;
        AuthPanel.IsVisible = hasAuth;

        if (kind == ProxySheetKind.Edit)
            FillStatus(proxy?.Status);
        UpdateSaveButton();
    }

    // ProxyStatus.vue: the record, one line each; nothing to show before the proxy was tried
    private void FillStatus(ProxyEndPointStatus? status)
    {
        var s = Strings.Current;
        StatusRows.Children.Clear();
        if (status == null || !status.HasUsed) {
            var none = new TextBlock { Text = s.NoData, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 24) };
            none.Classes.Add("body-medium");
            none.Classes.Add("disabled");
            StatusRows.Children.Add(none);
            return;
        }

        var (qualityText, qualityBrush) = ProxyQuality.Display(status.Quality);
        AddStatusRow(s.ProxyStatusQuality, qualityText, qualityBrush);
        AddStatusRow(s.ProxyStatusPenalty, status.Penalty.ToString(), null);
        AddStatusRow(s.ProxyStatusLatency, Format.Latency(status.Latency), null);
        AddStatusRow(s.ProxyStatusLastSucceeded, Format.RelativeTime(status.LastSucceeded), null);
        if (status.LastFailed != null)
            AddStatusRow(s.ProxyStatusLastFailed, Format.RelativeTime(status.LastFailed), null);
        if (!string.IsNullOrEmpty(status.ErrorMessage))
            AddStatusRow(s.Error, status.ErrorMessage, "ErrorBrush");
    }

    private void AddStatusRow(string label, string value, string? brushKey)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 36 };
        var labelText = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        labelText.Classes.Add("body-small");
        labelText.Classes.Add("disabled");
        var valueText = new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 260 };
        valueText.Classes.Add("body-small");
        if (brushKey == null) valueText.Classes.Add("disabled");
        else if (this.TryFindResource(brushKey, out var brush) && brush is IBrush valueBrush) valueText.Foreground = valueBrush;
        Grid.SetColumn(valueText, 1);
        row.Children.Add(labelText);
        row.Children.Add(valueText);
        StatusRows.Children.Add(row);
        var divider = new Border();
        divider.Classes.Add("divider");
        StatusRows.Children.Add(divider);
    }

    public override void FocusDefault()
    {
        if (_kind == ProxySheetKind.AddList) ListBox.LandFocus();
        else HostBox.LandFocus();
    }

    private string? HostRule => string.IsNullOrWhiteSpace(HostBox.Text) ? Strings.Current.ProxyRequiredHost : null;
    private string? PortRule => int.TryParse(PortBox.Text, out var port) && port is > 0 and <= 65535 ? null : Strings.Current.ProxyInvalidPort;

    private void UpdateSaveButton()
    {
        SaveButton.IsEnabled = !_isProcessing && (_kind == ProxySheetKind.AddList
            ? !string.IsNullOrWhiteSpace(ListBox.Text)
            : HostRule == null && PortRule == null);
    }

    private void OnFieldChanged(object? sender, TextChangedEventArgs e)
    {
        HostError.Text = HostRule;
        HostError.IsVisible = HostRule != null && HostBox.Text != null;
        PortError.Text = PortRule;
        PortError.IsVisible = PortRule != null;
        UpdateSaveButton();
    }

    // a whole address pasted into the host field fills the other fields (processHostField)
    private void OnHostLostFocus(object? sender, RoutedEventArgs e)
    {
        var text = HostBox.Text?.Trim();
        if (string.IsNullOrEmpty(text) || PortRule != null || _isProcessing)
            return;
        try {
            var parsed = ProxyEndPointParser.ParseHostToUrl(text, new ProxyEndPointDefaults {
                IsEnabled = EnabledSwitch.IsChecked,
                Protocol = _protocol,
                Port = int.Parse(PortBox.Text ?? "0"),
                Username = UsernameBox.Text,
                Password = PasswordBox.Text
            });
            var endPoint = ProxyEndPointParser.FromUrl(parsed);
            HostBox.Text = endPoint.Host;
            PortBox.Text = endPoint.Port.ToString();
            _protocol = endPoint.Protocol;
            ProtocolText.Text = _protocol.ToString().ToLowerInvariant();
            EnabledSwitch.IsChecked = endPoint.IsEnabled;
            if (!string.IsNullOrEmpty(endPoint.Username) || !string.IsNullOrEmpty(endPoint.Password)) {
                UsernameBox.Text = endPoint.Username ?? "";
                PasswordBox.Text = endPoint.Password ?? "";
                AuthSwitch.IsChecked = true;
                AuthPanel.IsVisible = true;
            }
        }
        catch (Exception) {
            // not an address the parser reads: the fields stay as typed and the rules say what is wrong
        }
    }

    private void OnEnabledClick(object? sender, RoutedEventArgs e)
    {
        EnabledSwitch.IsChecked = EnabledSwitch.IsChecked != true;
    }

    private void OnMenuButtonClick(object? sender, RoutedEventArgs e)
    {
        (sender as Button)?.FocusFirstMenuItem();
    }

    // turning the authentication off clears the credentials
    private void OnAuthClick(object? sender, RoutedEventArgs e)
    {
        var isOn = AuthSwitch.IsChecked != true;
        AuthSwitch.IsChecked = isOn;
        AuthPanel.IsVisible = isOn;
        if (!isOn) {
            UsernameBox.Text = "";
            PasswordBox.Text = "";
        }
    }

    private ProxyEndPoint BuildEndPoint()
    {
        return new ProxyEndPoint {
            Host = HostBox.Text?.Trim() ?? "",
            Port = int.Parse(PortBox.Text ?? "0"),
            Protocol = _protocol,
            IsEnabled = EnabledSwitch.IsChecked == true,
            Username = AuthSwitch.IsChecked == true ? UsernameBox.Text : null,
            Password = AuthSwitch.IsChecked == true ? PasswordBox.Text : null
        };
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try {
            _isProcessing = true;
            UpdateSaveButton();
            try {
                switch (_kind) {
                    case ProxySheetKind.Add:
                        await _service.Add(BuildEndPoint());
                        break;
                    case ProxySheetKind.AddList:
                        await _service.Import(ListBox.Text ?? "");
                        break;
                    default:
                        await _service.Update(_oldId ?? throw new InvalidOperationException("The proxy to update has no id."), BuildEndPoint());
                        break;
                }
                Close(true);
            }
            catch (Exception ex) {
                await _host.ProcessError(ex);
            }
            finally {
                _isProcessing = false;
                UpdateSaveButton();
            }
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (_oldId == null || !await _host.Confirm(Strings.Current.RemoveProxy, Strings.Current.RemoveProxyMsg))
                return;
            _isProcessing = true;
            UpdateSaveButton();
            try {
                await _service.Delete(_oldId);
                Close(true);
            }
            catch (Exception ex) {
                await _host.ProcessError(ex);
            }
            finally {
                _isProcessing = false;
                UpdateSaveButton();
            }
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
