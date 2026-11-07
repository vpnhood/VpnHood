using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
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
    public LogView()
    {
        InitializeComponent();

        // a TV has neither a share sheet nor anywhere to paste
        ShareIcon.IsVisible = VhApp.Intents.IsShareSupported;
        CopyGlyph.IsVisible = !VhApp.Intents.IsShareSupported;
        ShareButton.IsVisible = VhApp.Intents.IsShareSupported || !VhApp.IsTvUi;
        AutomationProperties.SetName(ShareButton, VhApp.Intents.IsShareSupported ? "Share" : "Copy");

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
            if (VhApp.Intents.IsShareSupported)
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
        // through the host's shell where it names one (a sandboxed app's, AvaloniaUiHosting.Shell)
        var text = LogText.Text ?? string.Empty;
        if (AvaloniaUiHosting.Shell is { } shell)
            shell.CopyText(text);
        else if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
        else
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
