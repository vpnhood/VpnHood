using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;

// What a screen reader says for a button, a list's item or a text box. Avalonia reads a lone
// TextBlock, and for any other content its type ("Avalonia.Controls.Grid") or its data's record
// ("LocationItem { VpnProfileId = ... }"); a text box it reads by no name at all. One without a name
// of its own is read by the words it shows instead: a button's or an item's joined as a web page's
// reader joins them ("Location, Auto Select"), a text box's placeholder - kept in step as they
// change. Icons, hidden parts and a button inside a button are not its words, so a button that
// shows only an icon, or a box whose placeholder is only an example, names itself.
internal static class AutomationNames
{
    // a control whose name came from its words, so a change rewrites it; a name the control was
    // given, set or bound, is left alone
    private static readonly AttachedProperty<bool> IsFromWordsProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsFromWords", typeof(AutomationNames));

    public static void Register()
    {
        Control.LoadedEvent.AddClassHandler<Button>((button, _) => Update(button));
        Control.LoadedEvent.AddClassHandler<ListBoxItem>((item, _) => Update(item));
        TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>((text, _) => UpdateOwner(text));
        Visual.IsVisibleProperty.Changed.AddClassHandler<Control>((control, _) => UpdateOwner(control));
        Control.LoadedEvent.AddClassHandler<TextBox>((textBox, _) => Update(textBox));
        TextBox.PlaceholderTextProperty.Changed.AddClassHandler<TextBox>((textBox, _) => {
            if (textBox.IsLoaded)
                Update(textBox);
        });
    }

    private static void UpdateOwner(Control control)
    {
        if (control.GetLogicalAncestors().FirstOrDefault(x => x is Button or ListBoxItem) is ContentControl { IsLoaded: true } owner)
            Update(owner);
    }

    // a button's or an item's words are those of what it shows: its content, or the template its
    // data is shown by
    private static void Update(ContentControl control)
    {
        if ((control.Content as Control ?? control.Presenter?.Child) is { } content and not TextBlock)
            SetName(control, string.Join(", ", Words(content)));
    }

    private static void Update(TextBox textBox)
    {
        SetName(textBox, textBox.PlaceholderText?.Trim() ?? "");
    }

    private static void SetName(Control control, string words)
    {
        if ((!control.GetValue(IsFromWordsProperty) && control.IsSet(AutomationProperties.NameProperty)) ||
            AutomationProperties.GetLabeledBy(control) != null)
            return;

        if (words.Length == 0) {
            control.ClearValue(AutomationProperties.NameProperty);
            control.ClearValue(IsFromWordsProperty);
            return;
        }

        AutomationProperties.SetName(control, words);
        control.SetValue(IsFromWordsProperty, true);
    }

    private static IEnumerable<string> Words(ILogical element)
    {
        switch (element) {
            case Control { IsVisible: false }:
            case Button: // read on its own
                yield break;

            case TextBlock textBlock:
                var text = WithoutIcons(textBlock.Text ?? textBlock.Inlines?.Text ?? "");
                if (text.Length > 0)
                    yield return text;
                yield break;
        }

        foreach (var child in element.LogicalChildren)
            foreach (var word in Words(child))
                yield return word;
    }

    // an icon font's glyph is a private-use character, which a screen reader has no word for
    private static string WithoutIcons(string text)
    {
        var words = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++) {
            var isPair = char.IsSurrogatePair(text, i);
            var code = isPair ? char.ConvertToUtf32(text, i) : text[i];
            if (code is not (>= 0xE000 and <= 0xF8FF or >= 0xF0000))
                words.Append(text, i, isPair ? 2 : 1);
            if (isPair)
                i++;
        }
        return words.ToString().Trim();
    }
}
