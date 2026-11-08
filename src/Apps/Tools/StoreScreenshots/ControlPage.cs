using Avalonia.Controls;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

namespace VpnHood.App.StoreScreenshots;

// A control that is no page - the menu, a message the app shows over a page - shown as one, so a
// picture of it can be drawn: the tool refuses a dialog over a page, and the menu has no route.
internal sealed class ControlPage : UserControl, IPage
{
    public ControlPage(Control content)
    {
        Content = content;
    }

    public void FocusDefault()
    {
    }
}
