using VpnHood.Client.Device;
using VpnHood.Client.Device.WinDivert;

// ReSharper disable once CheckNamespace
namespace VpnHood.Client.App.Maui;

public class WinAppProvider : IAppProvider
{
    public IDevice Device { get; } = new WinDivertDevice();
    public bool IsLogToConsoleSupported => false;
    public Uri? AdditionalUiUrl => WinApp.Instance.RegisterLocalDomain();

    public Uri UpdateInfoUrl => new ("https://github.com/vpnhood/VpnHood/releases/latest/download/VpnHoodClient-win-x64.json");
}