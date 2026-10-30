using Avalonia;
using Avalonia.Controls;

namespace VpnHood.AppUi.Hosting.Avalonia;

// A text box a key or a code is pasted into, which the platform's own input edits where a host has
// one and Avalonia's paste fails: the paired browser page over plain http (AvaloniaBrowserHost).
// Elsewhere it changes nothing.
public static class NativeTextInput
{
    public static readonly AttachedProperty<bool> IsPreferredProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("IsPreferred", typeof(NativeTextInput));

    public static bool GetIsPreferred(TextBox textBox)
    {
        return textBox.GetValue(IsPreferredProperty);
    }

    public static void SetIsPreferred(TextBox textBox, bool value)
    {
        textBox.SetValue(IsPreferredProperty, value);
    }
}
