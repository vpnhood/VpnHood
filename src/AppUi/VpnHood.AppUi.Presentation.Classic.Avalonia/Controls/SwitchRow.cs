using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

// A row whose press flips the switch in it (its Click handler does the flipping). The switch only
// shows: it takes no pointer, since a press on a live switch flipped it twice, and a screen reader
// hears the row as the toggle, with the switch's on or off, not a button beside a switch.
public class SwitchRow : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    // the switch the row flips: the first in its content
    public ToggleSwitch? Switch => this.GetLogicalDescendants().OfType<ToggleSwitch>().FirstOrDefault();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (Switch is not { } toggle)
            return;

        toggle.IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(toggle, AccessibilityView.Raw);
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new SwitchRowAutomationPeer(this);
    }
}
