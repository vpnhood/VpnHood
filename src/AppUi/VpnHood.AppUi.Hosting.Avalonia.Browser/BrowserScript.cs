using System.Runtime.InteropServices.JavaScript;
using System.Text;

namespace VpnHood.AppUi.Hosting.Avalonia.Browser;

// A page script of this library. It ships inside the assembly and is imported from its own text: a
// library has no folder of its own on the page.
internal static class BrowserScript
{
    public static async Task ImportAsync(string moduleName, CancellationToken cancellationToken)
    {
        await using var stream = typeof(BrowserScript).Assembly.GetManifestResourceStream(moduleName + ".js")
            ?? throw new InvalidOperationException($"The page script {moduleName}.js is not in the assembly.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var script = await reader.ReadToEndAsync(cancellationToken);
        await JSHost.ImportAsync(moduleName, "data:text/javascript;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(script)),
            cancellationToken);
    }
}
