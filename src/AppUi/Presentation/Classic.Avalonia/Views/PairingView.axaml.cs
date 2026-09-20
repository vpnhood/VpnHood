using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.App;
using VpnHood.AppUi.Services;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

// This page owns the listener the phone dials into: it starts it on arrival and stops it on the way
// out, whichever way that is (Done, Back, the host tearing the view down), and polls the presence
// list every two seconds while it is shown - the same contract as the web UI's dialog.
public partial class PairingView : UserControl, IPage, IDisposable
{
    private readonly MainView _host;
    private readonly DispatcherTimer _timer;
    private bool _isAlwaysOn;
    private bool _disposed;

    // The hint is what the web UI's dialog shows when it was opened for a job a remote cannot do -
    // adding a server, say: it names the page to open on the phone (RemoteAccessHint).
    public PairingView(MainView host, string? hint = null)
    {
        _host = host;
        InitializeComponent();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => _ = Refresh());
        AddressText.Text = Strings.Current.RemoteAccessStarting;
        HintText.Text = hint;
        HintText.IsVisible = hint != null;
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
            var state = await AppModel.Api.App.StartRemoteAccess(CancellationToken.None);
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
            Apply(await AppModel.Api.App.GetRemoteAccess(CancellationToken.None));
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
            AppModel.Api.App.StopRemoteAccess(CancellationToken.None).Forget("Could not stop remote access.");
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not stop remote access.");
        }
    }
}
