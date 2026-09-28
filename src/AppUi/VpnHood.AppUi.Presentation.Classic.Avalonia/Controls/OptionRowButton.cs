using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

// The press target of an option row (OptionRow), which a screen reader hears as the row's radio or
// checkbox, selected or not as its mark shows.
public class OptionRowButton : Button
{
    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<OptionRowButton, bool>(nameof(IsChecked));

    protected override Type StyleKeyOverride => typeof(Button);

    // a checkbox rather than a radio
    public bool IsCheckBox { get; set; }

    public bool IsChecked {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new OptionRowAutomationPeer(this);
    }
}
