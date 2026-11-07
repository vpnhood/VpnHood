using System.Runtime.InteropServices.JavaScript;

namespace VpnHood.AppUi.Hosting.Avalonia.Browser;

// BrowserExport.js: how the page hands the person something (AvaloniaUiHosting.DownloadText and
// CopyText). Avalonia's own copy stays where the browser gives the page a clipboard: https, or the
// device's own page on loopback.
internal static partial class BrowserExport
{
    private const string ModuleName = "VhBrowserExport";

    public static async Task RegisterAsync(CancellationToken cancellationToken)
    {
        await BrowserScript.ImportAsync(ModuleName, cancellationToken);
        AvaloniaUiHosting.DownloadText = Download;
        if (!HasClipboard())
            AvaloniaUiHosting.CopyText = CopyOrThrow;
    }

    private static bool HasClipboard()
    {
        using var navigator = JSHost.GlobalThis.GetPropertyAsJSObject("navigator");
        using var clipboard = navigator?.GetPropertyAsJSObject("clipboard");
        return clipboard != null;
    }

    private static void CopyOrThrow(string text)
    {
        if (!Copy(text))
            throw new InvalidOperationException("The browser did not copy the text.");
    }

    [JSImport("download", ModuleName)]
    private static partial void Download(string fileName, string text);

    [JSImport("copy", ModuleName)]
    private static partial bool Copy(string text);
}
