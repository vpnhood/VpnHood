using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls.Primitives;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

// A switch row to a screen reader: a toggle, on or off as its switch shows, which the reader
// flips by pressing the row, as a finger does. The new state is announced as the switch flips.
internal class SwitchRowAutomationPeer : ButtonAutomationPeer, IToggleProvider
{
    public SwitchRowAutomationPeer(SwitchRow owner)
        : base(owner)
    {
        if (owner.Switch is { } toggle)
            toggle.PropertyChanged += OnSwitchPropertyChanged;
    }

    public new SwitchRow Owner => (SwitchRow)base.Owner;

    public ToggleState ToggleState => ToState(Owner.Switch?.IsChecked);

    public void Toggle()
    {
        Invoke();
    }

    private void OnSwitchPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ToggleButton.IsCheckedProperty)
            RaisePropertyChangedEvent(TogglePatternIdentifiers.ToggleStateProperty,
                ToState((bool?)e.OldValue), ToState((bool?)e.NewValue));
    }

    private static ToggleState ToState(bool? isChecked)
    {
        return isChecked == true ? ToggleState.On : ToggleState.Off;
    }
}
