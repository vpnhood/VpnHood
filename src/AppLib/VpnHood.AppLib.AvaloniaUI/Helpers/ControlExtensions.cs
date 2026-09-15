using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.AvaloniaUI.Views;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.AvaloniaUI.Helpers;

internal static class ControlExtensions
{
    // the main view a control sits in: the host of the pages, the dialogs and the links
    public static MainView? FindHost(this Control control)
    {
        return control.FindAncestorOfType<MainView>();
    }

    // What an event handler that awaits does with an exception it did not expect: one that leaves
    // an async void method takes the process down, so every such handler ends in this, which shows
    // it the way the app shows any error (MainView.ProcessError) - or logs it, for a control that
    // has no host to show it in.
    public static Task ReportError(this Control control, Exception exception)
    {
        var host = control as MainView ?? control.FindHost();
        if (host != null)
            return host.ProcessError(exception);

        VhLogger.Instance.LogError(exception, "The UI caught an error outside a page host.");
        return Task.CompletedTask;
    }

    // the first button inside a control: where a page's input lands when the page is a list of
    // card rows
    public static Button? FindFirstButton(this Control control)
    {
        return control.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(x => x is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true, Focusable: true });
    }

    // A menu opened by a button: the input lands on its first item, as a remote must find the ring
    // in a menu it just opened (a flyout gives its presenter the focus, and Down from there is not
    // an item). Called from the button's click, after the flyout has opened.
    public static void FocusFirstMenuItem(this Button button)
    {
        if (button.Flyout is not Flyout { Content: Control content })
            return;
        Dispatcher.UIThread.Post(() => content.FindFirstButton()?.LandFocus(), DispatcherPriority.Loaded);
    }
}
