using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.AppUi.Common;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

// The app's log, read through the API as the web UI reads /api/app/log.txt. Nothing here is
// localized, as nothing is in the dialog this page comes from.
public partial class LogView : UserControl, IPage
{
    public LogView()
    {
        InitializeComponent();
        _ = Load();
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

    private async Task Load()
    {
        try {
            LogText.Text = await VhApp.Api.App.Log(CancellationToken.None);

            // the last lines are the ones being looked for, and they exist only once it is laid out
            Dispatcher.UIThread.Post(Scroller.ScrollToEnd, DispatcherPriority.Loaded);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not read the log.");
            LogText.Text = ex.Message;
        }
    }
}
