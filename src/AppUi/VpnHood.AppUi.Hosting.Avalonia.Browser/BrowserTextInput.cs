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
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace VpnHood.AppUi.Hosting.Avalonia.Browser;

// The page's own <input> over the text of a box a person types words or pastes into
// (NativeTextInput.IsPreferred), where the page cannot read the clipboard: plain http on a LAN address,
// the paired phone's page. The browser draws what is typed in the phone's own fonts, where Avalonia has
// only the faces the page ships, which leave a rare character a box; and it takes the phone's paste and
// autofill, where Avalonia 12 cannot paste: its read throws on the missing API, and its fallback reads
// the paste event's items after the browser has locked them (AvaloniaUI/Avalonia#22655); the fonts keep
// it after that fix. The box keeps its frame, its focus and its Text; Enter, Tab and Escape reach it as
// its own keys.
internal sealed class BrowserTextInput : NativeControlHost
{
    private readonly TextBox _textBox;
    private readonly Control _textArea;
    private readonly TextBlock? _placeholder;
    private JSObject? _input;
    private Rect _viewport = new(0, 0, double.PositiveInfinity, double.PositiveInfinity);

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
        EffectiveViewportChanged += OnEffectiveViewportChanged;
        UpdateShown();
        base.OnAttachedToVisualTree(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _textBox.GotFocus -= OnTextBoxGotFocus;
        _textBox.LostFocus -= OnTextBoxLostFocus;
        _textBox.PropertyChanged -= OnTextBoxPropertyChanged;
        EffectiveViewportChanged -= OnEffectiveViewportChanged;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
            UpdateClip();
    }

    // The part of the box its scrolling page shows. The page's input, drawn over everything Avalonia draws,
    // is cut to it: else a box scrolled under the page's header shows over it and takes its taps.
    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        _viewport = e.EffectiveViewport;
        UpdateClip();
    }

    private void UpdateClip()
    {
        var size = Bounds.Size;
        if (_input == null || size.Width <= 0 || size.Height <= 0)
            return;

        var shown = _viewport.Intersect(new Rect(size));
        if (shown.Width <= 0 || shown.Height <= 0)
            shown = new Rect(0, size.Height, size.Width, 0); // none of it: cut from the top down
        BrowserTextInputInterop.SetClip(_input, top: shown.Top, right: size.Width - shown.Right,
            bottom: size.Height - shown.Bottom, left: shown.Left);
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var input = BrowserTextInputInterop.Create(_textBox.TextWrapping != TextWrapping.NoWrap, _textBox.AcceptsReturn,
            OnInput, OnKey, OnFocus);
        BrowserTextInputInterop.SetText(input, _textBox.Text ?? "");
        SetLook(input);
        BrowserTextInputInterop.SetWords(input, _textBox.PlaceholderText ?? "", AutomationProperties.GetName(_textBox));
        SetOptions(input);
        _input = input;
        UpdateClip();
        // A box focused before its input was made - a dialog's first box, focused as it shows - hands its
        // focus on once the input is on the page; else the keys would reach neither.
        if (_textBox.IsFocused)
            Dispatcher.UIThread.Post(FocusInput, DispatcherPriority.Background);
        return new JSObjectControlHandle(input);
    }

    // The box's look as it is now. Set again as it changes: a box made while disabled - a list whose switch
    // is off - would otherwise keep its disabled grey once enabled.
    private void SetLook(JSObject input)
    {
        BrowserTextInputInterop.SetLook(input, CssFont(), CssColor(_textBox.Foreground), CssColor(_textBox.CaretBrush),
            CssColor(_textBox.PlaceholderForeground, _placeholder?.Opacity ?? 1), CssColor(_textBox.SelectionBrush),
            CssAlign(_textBox.TextAlignment), _textBox.FlowDirection == FlowDirection.RightToLeft ? "rtl" : "ltr");
    }

    private static bool IsLookProperty(AvaloniaProperty property)
    {
        return property == TextBox.ForegroundProperty || property == TextBox.CaretBrushProperty ||
               property == TextBox.PlaceholderForegroundProperty || property == TextBox.SelectionBrushProperty ||
               property == TextBox.FontFamilyProperty || property == TextBox.FontSizeProperty ||
               property == TextBox.FontWeightProperty || property == TextBox.FontStyleProperty ||
               property == TextBox.TextAlignmentProperty || property == FlowDirectionProperty;
    }

    // What the box takes, told to the phone's keyboard and autofill the way the box tells a platform's own
    // keyboard (TextInputOptions). A box that asks for nothing takes its text as typed, as a key or a code
    // is: no capitals, corrections or suggestions, and nothing filled in.
    private void SetOptions(JSObject input)
    {
        var contentType = TextInputOptions.GetContentType(_textBox);
        var enterKeyHint = TextInputOptions.GetReturnKeyType(_textBox) switch {
            TextInputReturnKeyType.Return => "enter",
            TextInputReturnKeyType.Done => "done",
            TextInputReturnKeyType.Go => "go",
            TextInputReturnKeyType.Send => "send",
            TextInputReturnKeyType.Search => "search",
            TextInputReturnKeyType.Next => "next",
            TextInputReturnKeyType.Previous => "previous",
            _ when _textBox.AcceptsReturn => "enter",
            _ when contentType == TextInputContentType.Search => "search",
            _ => "go" // Enter is the box's action: Add server's Add, a code's Activate
        };
        var inputMode = contentType switch {
            TextInputContentType.Email => "email",
            TextInputContentType.Url => "url",
            TextInputContentType.Search => "search",
            TextInputContentType.Digits or TextInputContentType.Pin => "numeric",
            TextInputContentType.Number => "decimal",
            _ => "text"
        };

        // masked as the box masks its own text
        BrowserTextInputInterop.SetOptions(input, isPassword: _textBox.PasswordChar != default, inputMode,
            NativeTextInput.GetAutoComplete(_textBox) ?? "off",
            suggestions: TextInputOptions.GetShowSuggestions(_textBox) == true,
            capitalize: TextInputOptions.GetAutoCapitalization(_textBox), enterKeyHint, _textBox.MaxLength);
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
        else if (_input != null && IsLookProperty(e.Property))
            SetLook(_input);
    }

    // Posted: Avalonia finishes moving its own focus first, and leaving one of its text boxes focuses
    // the page's container.
    private void OnTextBoxGotFocus(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(FocusInput);
    }

    // A box whose code selects its whole text as it lands there (Rename's name, so typing replaces it) has it
    // selected in the page's input too.
    private void FocusInput()
    {
        if (_input == null || !_textBox.IsFocused || !IsVisible)
            return;

        var length = _textBox.Text?.Length ?? 0;
        var isAllSelected = length > 0 && Math.Min(_textBox.SelectionStart, _textBox.SelectionEnd) == 0 &&
                            Math.Max(_textBox.SelectionStart, _textBox.SelectionEnd) == length;
        BrowserTextInputInterop.Focus(_input, isAllSelected);
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
