using Avalonia.Animation;
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
    private static readonly Transitions NoTransitions = new();

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

    // A row a list reuses for another item jumps to that item's state: the knob's slide would play on
    // rows as they scroll into view
    protected override void OnDataContextBeginUpdate()
    {
        base.OnDataContextBeginUpdate();
        if (Switch is { } toggle)
            toggle.KnobTransitions = NoTransitions;
    }

    protected override void OnDataContextEndUpdate()
    {
        base.OnDataContextEndUpdate();
        Switch?.ClearValue(ToggleSwitch.KnobTransitionsProperty);
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new SwitchRowAutomationPeer(this);
    }
}
