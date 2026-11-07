using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.AppUi.Common;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

// The app's log, read through the API. Nothing here is localized.
public partial class LogView : UserControl, IPage
{
    // A remote is a browser on another device: a share sheet would open on the device it drives, far
    // from the person, and the page is plain http, where a browser gives it no clipboard and no share.
    private static bool IsDownload => VhApp.IsRemote;
    private static bool IsShare => VhApp.Intents.IsShareSupported && !VhApp.IsRemote;

    public LogView()
    {
        InitializeComponent();

        // a TV has neither a share sheet nor anywhere to paste
        ShareIcon.IsVisible = IsShare;
        DownloadIcon.IsVisible = IsDownload;
        CopyGlyph.IsVisible = !IsShare && !IsDownload;
        ShareButton.IsVisible = IsShare || !VhApp.IsTvUi;
        AutomationProperties.SetName(ShareButton, IsDownload ? "Download" : IsShare ? "Share" : "Copy");

        _ = Load(CancellationToken.None);
    }

    // The page takes the focus itself, so the keys below reach it: the log has no control to aim
    // at, and a TV has no back button either.
    public void FocusDefault()
    {
        Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key) {
            case Key.Up:
                Scroller.LineUp();
                e.Handled = true;
                break;
            case Key.Down:
                Scroller.LineDown();
                e.Handled = true;
                break;
            case Key.PageUp:
                Scroller.PageUp();
                e.Handled = true;
                break;
            case Key.PageDown:
                Scroller.PageDown();
                e.Handled = true;
                break;
        }

        base.OnKeyDown(e);
    }

    private async void OnShareClick(object? sender, RoutedEventArgs e)
    {
        try {
            if (IsDownload)
                AvaloniaUiHosting.DownloadText?.Invoke("report.txt", LogText.Text ?? string.Empty);
            else if (IsShare)
                await VhApp.Api.Intents.ShareLog(CancellationToken.None);
            else
                await Copy(CancellationToken.None);
        }
        catch (Exception ex) {
            await this.ReportError(ex);
        }
    }

    private async Task Copy(CancellationToken cancellationToken)
    {
        if (!await this.CopyText(LogText.Text ?? string.Empty))
            return;

        CopyGlyph.Text = Mdi.Check;
        await Task.Delay(2000, cancellationToken);
        CopyGlyph.Text = Mdi.ContentCopy;
    }

    private async Task Load(CancellationToken cancellationToken)
    {
        try {
            LogText.Text = await VhApp.Api.App.Log(cancellationToken);

            // the last lines are the ones being looked for, and they exist only once it is laid out
            Dispatcher.UIThread.Post(Scroller.ScrollToEnd, DispatcherPriority.Loaded);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not read the log.");
            LogText.Text = ex.Message;
        }
    }
}
