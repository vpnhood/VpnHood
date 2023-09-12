using VpnHood.Client.Device;
using VpnHood.Client.Device.Android;

namespace VpnHood.Client.App.Maui;

internal class AndroidAppProvider : IAppProvider
{
    public IDevice Device { get; } = new AndroidDevice();
    public bool IsLogToConsoleSupported => true;
    public Uri? AdditionalUiUrl => null;
    public Uri? UpdateInfoUrl => null;
}