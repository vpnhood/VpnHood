using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.ClassicAvaloniaUi.Helpers;
using VpnHood.AppLib.Contracts.Proxies;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Controls;

public partial class ProxyListItem : UserControl
{
    public event EventHandler? Clicked;

    public ProxyListItem(AppProxyEndPointInfo proxy, bool isLast)
    {
        InitializeComponent();
        Proxy = proxy;
        Row.Classes.Set("last", isLast);

        var s = Strings.Current;
        var endPoint = proxy.EndPoint;
        var status = proxy.Status;
        ProtocolText.Text = endPoint.Protocol.ToString().ToLowerInvariant();
        TitleText.Text = string.IsNullOrEmpty(endPoint.Username)
            ? $"{endPoint.Host}:{endPoint.Port}"
            : $"{endPoint.Host}:{endPoint.Port} · {endPoint.Username}";
        StateText.Text = endPoint.IsEnabled ? s.On : s.Off;
        StateChip.Classes.Set("status-on", endPoint.IsEnabled);
        StateChip.Classes.Set("status-off", !endPoint.IsEnabled);

        SucceededText.Text = status.SucceededCount.ToString();
        SucceededIcon.Classes.Set("disabled", status.SucceededCount == 0);
        if (status.SucceededCount > 0) SucceededIcon.Foreground = Brush("EnablePremiumBrush");
        FailedText.Text = status.FailedCount.ToString();
        FailedIcon.Classes.Set("disabled", status.FailedCount == 0);
        if (status.FailedCount > 0) FailedIcon.Foreground = Brush("ErrorBrush");

        var (qualityText, qualityBrush) = ProxyQuality.Display(status.HasUsed ? status.Quality : null);
        QualityText.Text = qualityText;
        QualityText.IsVisible = status.HasUsed;
        QualityDivider.IsVisible = status.HasUsed;
        if (qualityBrush != null) QualityText.Foreground = Brush(qualityBrush);

        ErrorText.Text = status.ErrorMessage;
        ErrorText.IsVisible = !string.IsNullOrEmpty(status.ErrorMessage);
    }

    public AppProxyEndPointInfo Proxy { get; }

    private IBrush? Brush(string key)
    {
        return this.TryFindResource(key, out var value) && value is IBrush brush ? brush : null;
    }

    public void LandFocus()
    {
        Row.LandFocus();
    }

    private void OnRowClick(object? sender, RoutedEventArgs e)
    {
        Clicked?.Invoke(this, EventArgs.Empty);
    }
}
