using Microsoft.Extensions.Logging;

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
        var appProvider = CreateAppProvider();
        VpnHoodApp.Init(appProvider, new AppOptions
        {
            AppDataFolderPath = AppLocalDataPath,
            UpdateInfoUrl = new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-win-x64.json")
        });
        VpnHoodAppUi.Init(new MemoryStream(Resource.SPA), url2: localWebUrl);


        return builder.Build();
    }

    // Create vpnhood IAppProvider 
    private static IAppProvider CreateAppProvider()
    {
        return new WinAppProvider();
    }

}