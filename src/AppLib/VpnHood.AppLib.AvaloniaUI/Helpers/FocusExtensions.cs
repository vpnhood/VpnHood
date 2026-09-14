using Avalonia.Controls;
using Avalonia.Input;

namespace VpnHood.AppLib.AvaloniaUI.Helpers;

// Where the input starts on a page. A TV's remote has no pointer, so the landing must light the
// ring at once: a directional focus, which Avalonia shows as :focus-visible. Anywhere else the
// focus is placed silently - a finger would not want a ring - and the ring comes with the first
// key, so a keyboard still starts from the right control without a Tab first.
internal static class FocusExtensions
{
    public static void LandFocus(this Control control)
    {
        var method = VpnHoodApp.Instance.Features.IsTv
            ? NavigationMethod.Directional
            : NavigationMethod.Unspecified;
        control.Focus(method);
    }
}
