using Avalonia;
using Avalonia.Controls;

namespace VpnHood.AppUi.Hosting.Avalonia;

/// <summary>
/// Provides the attached properties that ask for the platform's own input on a text box, where a host has one, as the
/// paired browser page over plain http does.
/// </summary>
/// <remarks>
/// There the browser draws what is typed in the phone's own fonts, and takes the phone's paste and autofill, which
/// Avalonia cannot. It is for a box a person types words or pastes into; a host without such input ignores it.
/// </remarks>
public static class NativeTextInput
{
    /// <summary>Identifies the <c>IsPreferred</c> attached property: whether the box asks for the platform's own input.</summary>
    public static readonly AttachedProperty<bool> IsPreferredProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("IsPreferred", typeof(NativeTextInput));

    /// <summary>
    /// Identifies the <c>AutoComplete</c> attached property: what the box asks the platform to offer to fill it with, by a
    /// browser's autocomplete hint, as <c>username</c>, <c>current-password</c> or <c>one-time-code</c>; with none, it asks
    /// for nothing to be offered, which a browser may disregard.
    /// </summary>
    public static readonly AttachedProperty<string?> AutoCompleteProperty =
        AvaloniaProperty.RegisterAttached<TextBox, string?>("AutoComplete", typeof(NativeTextInput));

    /// <summary>Gets whether a text box asks for the platform's own input.</summary>
    /// <param name="textBox">The text box.</param>
    /// <returns><see langword="true"/> where it asks.</returns>
    public static bool GetIsPreferred(TextBox textBox)
    {
        return textBox.GetValue(IsPreferredProperty);
    }

    /// <summary>Sets whether a text box asks for the platform's own input.</summary>
    /// <param name="textBox">The text box.</param>
    /// <param name="value"><see langword="true"/> to ask for it.</param>
    public static void SetIsPreferred(TextBox textBox, bool value)
    {
        textBox.SetValue(IsPreferredProperty, value);
    }

    /// <summary>Gets what a text box asks the platform to offer to fill it with.</summary>
    /// <param name="textBox">The text box.</param>
    /// <returns>The autocomplete hint, or <see langword="null"/>.</returns>
    public static string? GetAutoComplete(TextBox textBox)
    {
        return textBox.GetValue(AutoCompleteProperty);
    }

    /// <summary>Sets what a text box asks the platform to offer to fill it with.</summary>
    /// <param name="textBox">The text box.</param>
    /// <param name="value">The autocomplete hint, or <see langword="null"/> to ask for none.</param>
    public static void SetAutoComplete(TextBox textBox, string? value)
    {
        textBox.SetValue(AutoCompleteProperty, value);
    }
}
