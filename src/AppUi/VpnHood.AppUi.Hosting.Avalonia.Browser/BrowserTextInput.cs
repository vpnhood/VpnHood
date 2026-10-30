using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Browser;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace VpnHood.AppUi.Hosting.Avalonia.Browser;

// The page's own <input> over the text of a box a key or a code is pasted into
// (NativeTextInput.IsPreferred), where the page cannot read the clipboard (plain http on a LAN
// address) and Avalonia 12 cannot paste: its read throws on the missing API, and its fallback reads
// the paste event's items after the browser has locked them. The box keeps its frame, its focus and
// its Text; Enter, Tab and Escape reach it as its own keys.
// TODO Drop it once Avalonia pastes on such a page.
internal sealed class BrowserTextInput : NativeControlHost
{
    private readonly TextBox _textBox;
    private readonly Control _textArea;
    private readonly TextBlock? _placeholder;
    private JSObject? _input;

    private BrowserTextInput(TextBox textBox, Control textArea, TextBlock? placeholder)
    {
        _textBox = textBox;
        _textArea = textArea;
        _placeholder = placeholder;
        this[!MarginProperty] = textArea[!MarginProperty];
    }

    public static async Task RegisterAsync(CancellationToken cancellationToken)
    {
        if (CanReadClipboard())
            return;

        await BrowserTextInputInterop.ImportAsync(cancellationToken);
        NativeTextInput.IsPreferredProperty.Changed.AddClassHandler<TextBox, bool>(OnIsPreferredChanged);
    }

    // Avalonia's paste works where the page can read the clipboard: https, or the device's own page on loopback
    private static bool CanReadClipboard()
    {
        using var navigator = JSHost.GlobalThis.GetPropertyAsJSObject("navigator");
        using var clipboard = navigator?.GetPropertyAsJSObject("clipboard");
        return clipboard != null && (clipboard.HasProperty("read") || clipboard.HasProperty("readText"));
    }

    // set once, in the UI's markup
    private static void OnIsPreferredChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs<bool> e)
    {
        if (!e.NewValue.GetValueOrDefault())
            return;

        // Avalonia's own input element must never take the browser's focus: Avalonia moves it there at
        // every move of its caret, which would take the keys from the page's input mid-word
        InputMethod.SetIsInputMethodEnabled(textBox, false);
        textBox.TemplateApplied += OnTemplateApplied;
    }

    // Over the template's text area (Fluent's), in its cell and its margin, so the box's frame stays
    // Avalonia's. A template applied again discards this one with it.
    private static void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        var textArea = e.NameScope.Find<Control>("PART_InnerDockPanel")
            ?? throw new InvalidOperationException("The text box's template has no PART_InnerDockPanel for the page's input to cover.");
        var cell = textArea.GetVisualParent() as Grid
            ?? throw new InvalidOperationException("The text box's text area is not in a Grid the page's input can share.");

        var input = new BrowserTextInput(textBox, textArea, e.NameScope.Find<TextBlock>("PART_Placeholder"));
        Grid.SetColumn(input, Grid.GetColumn(textArea));
        cell.Children.Add(input);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _textBox.GotFocus += OnTextBoxGotFocus;
        _textBox.LostFocus += OnTextBoxLostFocus;
        _textBox.PropertyChanged += OnTextBoxPropertyChanged;
        UpdateShown();
        base.OnAttachedToVisualTree(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _textBox.GotFocus -= OnTextBoxGotFocus;
        _textBox.LostFocus -= OnTextBoxLostFocus;
        _textBox.PropertyChanged -= OnTextBoxPropertyChanged;
        base.OnDetachedFromVisualTree(e);
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var input = BrowserTextInputInterop.Create(OnInput, OnKey, OnFocus);
        BrowserTextInputInterop.SetText(input, _textBox.Text ?? "");
        BrowserTextInputInterop.SetLook(input, CssFont(), CssColor(_textBox.Foreground), CssColor(_textBox.CaretBrush),
            CssColor(_textBox.PlaceholderForeground, _placeholder?.Opacity ?? 1), CssColor(_textBox.SelectionBrush),
            CssAlign(_textBox.TextAlignment), _textBox.FlowDirection == FlowDirection.RightToLeft ? "rtl" : "ltr");
        BrowserTextInputInterop.SetWords(input, _textBox.PlaceholderText ?? "", AutomationProperties.GetName(_textBox));
        _input = input;
        return new JSObjectControlHandle(input);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _input = null;
        base.DestroyNativeControlCore(control);
    }

    // A disabled box sits under a dialog or the drawer, and the page's input, drawn over everything
    // Avalonia draws, would show through it: there the box's own text shows instead.
    private void UpdateShown()
    {
        IsVisible = _textBox.IsEffectivelyEnabled;
        _textArea.Opacity = IsVisible ? 0 : 1;
    }

    private void OnTextBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty && _input != null)
            BrowserTextInputInterop.SetText(_input, _textBox.Text ?? "");
        else if (e.Property == IsEffectivelyEnabledProperty)
            UpdateShown();
    }

    // Posted: Avalonia finishes moving its own focus first, and leaving one of its text boxes focuses
    // the page's container.
    private void OnTextBoxGotFocus(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => {
            if (_input != null && _textBox.IsFocused && IsVisible)
                BrowserTextInputInterop.Focus(_input);
        });
    }

    private void OnTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_input != null)
            BrowserTextInputInterop.Release(_input);
    }

    // a click in the input: the box takes Avalonia's focus, whose ring is the box's
    private void OnFocus()
    {
        if (!_textBox.IsFocused)
            _textBox.Focus(NavigationMethod.Pointer);
    }

    private void OnInput(string text)
    {
        _textBox.Text = text;
    }

    private void OnKey(string key, bool shift)
    {
        _textBox.RaiseEvent(new KeyEventArgs {
            RoutedEvent = KeyDownEvent,
            Key = key switch { "Enter" => Key.Enter, "Tab" => Key.Tab, _ => Key.Escape },
            KeyModifiers = shift ? KeyModifiers.Shift : KeyModifiers.None
        });
    }

    // the box's own face; the page names the faces it serves (index.html's @font-face)
    private string CssFont()
    {
        var families = string.Join(", ", _textBox.FontFamily.FamilyNames.Select(x => $"\"{x}\""));
        var style = _textBox.FontStyle == FontStyle.Normal ? "" : "italic ";
        return string.Create(CultureInfo.InvariantCulture,
            $"{style}{(int)_textBox.FontWeight} {_textBox.FontSize}px {families}, sans-serif");
    }

    // a solid brush; any other leaves the browser's own colour
    private static string CssColor(IBrush? brush, double opacity = 1)
    {
        if (brush is not ISolidColorBrush solid)
            return "";

        var color = solid.Color;
        return string.Create(CultureInfo.InvariantCulture,
            $"rgba({color.R}, {color.G}, {color.B}, {color.A / 255d * solid.Opacity * opacity:0.###})");
    }

    private static string CssAlign(TextAlignment alignment)
    {
        return alignment switch {
            TextAlignment.Left => "left",
            TextAlignment.Center => "center",
            TextAlignment.Right => "right",
            TextAlignment.End => "end",
            TextAlignment.Justify => "justify",
            _ => "start"
        };
    }
}
