using Avalonia.Controls;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;

// Says a message through a text a screen reader watches (AutomationProperties.LiveSetting), which
// speaks when the text's words change. So the text shows before its words change, and the same
// words again - a second failed try - are cleared first, else the reader hears nothing new; Narrator
// is told twice then (cleared, then the words), which beats silence.
//
// What Avalonia 12.1.3 gives: on Windows (and macOS) a text a reader has already visited is said as
// it changes; one it never visited is silent, since Avalonia makes a control's Windows automation
// object only when a reader walks to it - so a snackbar or an error is silent the first time it
// shows, and a new dialog's message never. Android and iOS have no live texts at all. Avalonia's to
// fix, not worked around here.
internal static class LiveText
{
    public static void Say(TextBlock textBlock, string text)
    {
        textBlock.IsVisible = true;
        if (textBlock.Text == text)
            textBlock.Text = "";

        textBlock.Text = text;
    }
}
