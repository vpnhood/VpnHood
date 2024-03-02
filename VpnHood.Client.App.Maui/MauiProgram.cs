using Microsoft.Extensions.Logging;
using VpnHood.Client.App;
using VpnHood.Client.App.Resources;
using VpnHood.Client.App.WebServer;
using VpnHood.Client.Device;

namespace VpnHood.Client.Samples.MauiAppSpaSample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp(IDevice vpnHoodDevice)
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var resources = VpnHoodAppResource.Resources;
        resources.Strings.AppName = "VpnHood Client Sample";
        VpnHoodApp.Init(vpnHoodDevice, new AppOptions { Resources = resources });

        // init web server with spa zip data
        ArgumentNullException.ThrowIfNull(VpnHoodApp.Instance.Resources.SpaZipData);
        using var memoryStream = new MemoryStream(VpnHoodApp.Instance.Resources.SpaZipData);
        VpnHoodAppWebServer.Init(memoryStream);

        return builder.Build();
    }
}