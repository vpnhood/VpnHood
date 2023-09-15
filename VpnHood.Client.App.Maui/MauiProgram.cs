using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Resources;
using VpnHood.Client.App.WebServer;
namespace VpnHood.Client.App.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
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

        var appProvider = CreateAppProvider();
        using var spaResource = new MemoryStream(UiResource.SPA);
        VpnHoodApp.Init(appProvider, new AppOptions { UpdateInfoUrl = appProvider.UpdateInfoUrl });
        VpnHoodAppWebServer.Init(spaResource, url2: appProvider.AdditionalUiUrl);

        return builder.Build();
    }

    private static IAppProvider CreateAppProvider()
    {
#if WINDOWS
        return new WinAppProvider();
#elif ANDROID
        return new AppProvider();
#else
        throw new PlatformNotSupportedException();
#endif
    }
}
