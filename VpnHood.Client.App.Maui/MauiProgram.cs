using Microsoft.Extensions.Logging;
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
        var appProvider = CreateAppProvider();
        VpnHoodApp.Init(appProvider, new AppOptions { UpdateInfoUrl = GetUpdateInfoUrl() });
        VpnHoodAppUi.Init(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "SPA.zip")));

        return builder.Build();
    }

    // Create VpnHood IAppProvider 
    private static IAppProvider CreateAppProvider()
    {
#if WINDOWS
        return new WinAppProvider();
#else
        throw new NotSupportedException();
#endif

    }

    private static Uri GetUpdateInfoUrl()
    {
#if WINDOWS
        return new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-win-x64.json");
#elif ANDROID
        return new Uri("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-android.json");
#else
        throw new NotSupportedException();
#endif
    }
}