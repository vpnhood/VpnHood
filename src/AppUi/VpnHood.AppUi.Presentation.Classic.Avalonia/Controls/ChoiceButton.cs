using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

// A button that is one of a group's choices - a radio: an option row, a plan - or a checkbox, which
// a screen reader hears with its state, selected or not. The page marks the choice (IsChecked) and
// styles it as before.
public class ChoiceButton : Button
{
    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<ChoiceButton, bool>(nameof(IsChecked));

    protected override Type StyleKeyOverride => typeof(Button);

    // a checkbox rather than a radio
    public bool IsCheckBox { get; set; }

    public bool IsChecked {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new ChoiceButtonAutomationPeer(this);
    }
}
