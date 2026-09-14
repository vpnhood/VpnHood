using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.AppLib.WebServer;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.AvaloniaUI.Views;

// This page owns the listener the phone dials into: it starts it on arrival and stops it on the way
// out, whichever way that is (Done, Back, the host tearing the view down), and polls the presence
// list every two seconds while it is shown - the same contract as the web UI's dialog.
public partial class PairingView : UserControl, IPage, IDisposable
{
    private readonly MainView _host;
    private readonly DispatcherTimer _timer;
    private bool _isAlwaysOn;
    private bool _disposed;

    public PairingView(MainView host)
    {
        _host = host;
        InitializeComponent();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => _ = Refresh());
        AddressText.Text = Strings.Current.RemoteAccessStarting;
    }

    public void FocusDefault()
    {
        DoneButton.LandFocus();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ = Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Dispose();
        base.OnDetachedFromVisualTree(e);
    }

    private async Task Start()
    {
        try {
            var state = await VpnHoodAppWebServer.Instance.StartRemoteAccess();
            Apply(state);
            _timer.Start();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not start remote access.");
            AddressText.Text = ex.Message;
        }
    }

    private async Task Refresh()
    {
        if (_disposed)
            return;
        try {
            Apply(await VpnHoodAppWebServer.Instance.RefreshRemoteAccess());
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not refresh remote access.");
        }
    }

    private void Apply(RemoteAccessState state)
    {
        _isAlwaysOn = state.IsAlwaysOn;
        var url = state.Urls.FirstOrDefault();
        Qr.Text = url?.AbsoluteUri;
        AddressText.Text = url == null
            ? Strings.Current.RemoteAccessStarting
            : string.Join("\n", state.Urls.Select(x => x.AbsoluteUri));
        ConnectedText.Text = state.ConnectedDevices.Length == 0
            ? Strings.Current.RemoteAccessNoDevice
            : Strings.Current.RemoteAccessConnectedFrom(string.Join(", ", state.ConnectedDevices.Select(x => x.ToString())));
    }

    private void OnDoneClick(object? sender, RoutedEventArgs e)
    {
        _host.GoBack();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();

        // a developer's always-on listener is nobody's to stop
        if (_isAlwaysOn)
            return;
        try {
            VpnHoodAppWebServer.Instance.StopRemoteAccess();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not stop remote access.");
        }
    }
}
