using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Resources;
using VpnHood.Client.Device;
using VpnHood.Client.Device.WinDivert;

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

        var resources = VpnHoodAppResource.Resources;
        VpnHoodApp.Init(CreateDevice(), new AppOptions() { Resources = resources });

        return builder.Build();
    }

    private static IDevice CreateDevice()
    {
#if WINDOWS
        return new WinDivertDevice();
#elif ANDROID
        return new AppProvider();
#else
        throw new PlatformNotSupportedException();
#endif
    }
}
