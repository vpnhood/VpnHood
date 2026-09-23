using Avalonia;
using VpnHood.AppUi.Hosting.Avalonia.Browser;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Browser;

// The Classic Avalonia UI as a browser page. The host does everything (AvaloniaBrowserHost); this
// names the UI it mounts, the way a head's desktop or Android host names it.
internal static class Program
{
    private static Task Main(string[] args)
    {
        return AvaloniaBrowserHost.RunAsync<ClassicAvaloniaApp>(args);
    }

    // Avalonia's designer and previewer look for this by name.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<ClassicAvaloniaApp>();
    }
}
