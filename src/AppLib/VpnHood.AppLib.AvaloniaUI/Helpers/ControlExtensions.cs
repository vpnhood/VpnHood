using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VpnHood.AppLib.AvaloniaUI.Views;

namespace VpnHood.AppLib.AvaloniaUI.Helpers;

internal static class ControlExtensions
{
    // the main view a control sits in: the host of the pages, the dialogs and the links
    public static MainView? FindHost(this Control control)
    {
        return control.FindAncestorOfType<MainView>();
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
