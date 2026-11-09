using Avalonia;
using Avalonia.Controls;

namespace VpnHood.AppUi.Hosting.Avalonia;

// A text box that the platform's own input edits where a host has one: the paired browser page over plain
// http (AvaloniaBrowserHost). There the browser draws what is typed in the phone's own fonts, where Avalonia
// has only the faces the page ships, and takes the phone's paste and autofill, where Avalonia cannot paste.
// For a box a person types words into or pastes into. Elsewhere it changes nothing.
public static class NativeTextInput
{
    public static readonly AttachedProperty<bool> IsPreferredProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("IsPreferred", typeof(NativeTextInput));

    // What the platform may fill the box with, by a browser's autocomplete token: "username",
    // "current-password", "one-time-code". None: it offers nothing.
    public static readonly AttachedProperty<string?> AutoCompleteProperty =
        AvaloniaProperty.RegisterAttached<TextBox, string?>("AutoComplete", typeof(NativeTextInput));

    public static bool GetIsPreferred(TextBox textBox)
    {
        return textBox.GetValue(IsPreferredProperty);
    }

    public static void SetIsPreferred(TextBox textBox, bool value)
    {
        textBox.SetValue(IsPreferredProperty, value);
    }

    public static string? GetAutoComplete(TextBox textBox)
    {
        return textBox.GetValue(AutoCompleteProperty);
    }

    public static void SetAutoComplete(TextBox textBox, string? value)
    {
        textBox.SetValue(AutoCompleteProperty, value);
    }
}
