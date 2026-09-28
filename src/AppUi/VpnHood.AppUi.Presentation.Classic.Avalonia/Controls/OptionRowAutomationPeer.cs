using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

// An option row to a screen reader, as Avalonia's own radio button is: a radio, selected or not,
// or a checkbox, on or off. Choosing it presses the row, as a finger does, and the page marks the
// choice; the new state is announced as the mark changes.
internal class OptionRowAutomationPeer : ButtonAutomationPeer, IToggleProvider, ISelectionItemProvider
{
    public OptionRowAutomationPeer(OptionRowButton owner)
        : base(owner)
    {
        owner.PropertyChanged += OnOwnerPropertyChanged;
    }

    public new OptionRowButton Owner => (OptionRowButton)base.Owner;

    public ToggleState ToggleState => ToState(Owner.IsChecked);

    public bool IsSelected => Owner.IsChecked;

    public ISelectionProvider? SelectionContainer => null;

    public void Toggle()
    {
        Invoke();
    }

    public void Select()
    {
        if (!Owner.IsChecked)
            Invoke();
    }

    public void AddToSelection()
    {
        if (!Owner.IsChecked)
            throw new InvalidOperationException("A radio row joins the selection by being selected.");
    }

    public void RemoveFromSelection()
    {
        if (Owner.IsChecked)
            throw new InvalidOperationException("A radio row leaves the selection when another is selected.");
    }

    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return Owner.IsCheckBox ? AutomationControlType.CheckBox : AutomationControlType.RadioButton;
    }

    // a checkbox is no choice among a group's
    protected override object? GetProviderCore(Type providerType)
    {
        return providerType == typeof(ISelectionItemProvider) && Owner.IsCheckBox
            ? null
            : base.GetProviderCore(providerType);
    }

    private void OnOwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != OptionRowButton.IsCheckedProperty)
            return;

        var wasChecked = e.OldValue is true;
        var isChecked = e.NewValue is true;
        RaisePropertyChangedEvent(TogglePatternIdentifiers.ToggleStateProperty, ToState(wasChecked), ToState(isChecked));
        if (!Owner.IsCheckBox)
            RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, wasChecked, isChecked);
    }

    private static ToggleState ToState(bool isChecked)
    {
        return isChecked ? ToggleState.On : ToggleState.Off;
    }
}
