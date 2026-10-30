using System.Runtime.InteropServices.JavaScript;
using System.Text;

namespace VpnHood.AppUi.Hosting.Avalonia.Browser;

// BrowserTextInput.js, the page's side of BrowserTextInput. The script ships inside this assembly
// and is imported from its own text: a library has no folder of its own on the page.
internal static partial class BrowserTextInputInterop
{
    private const string ModuleName = "VhBrowserTextInput";

    public static async Task ImportAsync(CancellationToken cancellationToken)
    {
        await using var stream = typeof(BrowserTextInputInterop).Assembly.GetManifestResourceStream(ModuleName + ".js")
            ?? throw new InvalidOperationException($"The page script {ModuleName}.js is not in the assembly.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var script = await reader.ReadToEndAsync(cancellationToken);
        await JSHost.ImportAsync(ModuleName, "data:text/javascript;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(script)),
            cancellationToken);
    }

    [JSImport("create", ModuleName)]
    public static partial JSObject Create(
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

    [JSImport("focus", ModuleName)]
    public static partial void Focus(JSObject input);

    [JSImport("release", ModuleName)]
    public static partial void Release(JSObject input);
}
