using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Resources;
using VpnHood.Client.App.UI;
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

        // init app
#if WINDOWS
        var appProvider = new VpnHoodAppProvider();
        using var spaResource = new MemoryStream(UiResource.SPA);
        VpnHoodApp.Init(appProvider, new AppOptions { UpdateInfoUrl = appProvider.UpdateInfoUrl });
        VpnHoodAppUi.Init(spaResource, url2: appProvider.AdditionalUiUrl);
#endif

        return builder.Build();
    }
}
