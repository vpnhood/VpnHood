using System.Runtime.InteropServices.JavaScript;

namespace VpnHood.AppUi.Hosting.Avalonia.Browser;

// BrowserTextInput.js, the page's side of BrowserTextInput.
internal static partial class BrowserTextInputInterop
{
    private const string ModuleName = "VhBrowserTextInput";

    public static Task ImportAsync(CancellationToken cancellationToken)
    {
        return BrowserScript.ImportAsync(ModuleName, cancellationToken);
    }

    [JSImport("create", ModuleName)]
    public static partial JSObject Create(bool multiline, bool acceptsReturn,
        [JSMarshalAs<JSType.Function<JSType.String>>] Action<string> onInput,
        [JSMarshalAs<JSType.Function<JSType.String, JSType.Boolean>>] Action<string, bool> onKey,
        [JSMarshalAs<JSType.Function>] Action onFocus);

    [JSImport("setText", ModuleName)]
    public static partial void SetText(JSObject input, string text);

    [JSImport("setLook", ModuleName)]
    public static partial void SetLook(JSObject input, string font, string color, string caret, string placeholder,
        string selection, string align, string direction);

    [JSImport("setWords", ModuleName)]
    public static partial void SetWords(JSObject input, string placeholder, string? label);

    [JSImport("setOptions", ModuleName)]
    public static partial void SetOptions(JSObject input, bool isPassword, string inputMode, string autoComplete,
        bool suggestions, bool capitalize, string enterKeyHint, int maxLength);

    [JSImport("setClip", ModuleName)]
    public static partial void SetClip(JSObject input, double top, double right, double bottom, double left);

    [JSImport("focus", ModuleName)]
    public static partial void Focus(JSObject input, bool selectAll);

    [JSImport("release", ModuleName)]
    public static partial void Release(JSObject input);
}
