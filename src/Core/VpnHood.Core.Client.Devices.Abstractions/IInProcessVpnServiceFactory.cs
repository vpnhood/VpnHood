using VpnHood.Core.Client.VpnServices.Abstractions;

namespace VpnHood.Core.Client.Devices.Abstractions;

// The VPN service in the app's own process, where a desktop device starts no VPN host: a debugger's run (dev,
// AvaloniaUI.Dev) or a MAUI app on Windows. The device makes it at a connect, with its own folder and kill switch.
// The device does not reference the VPN service's side, so the head, which references both, hands it this factory
// (WindowsVpnServiceFactory, LinuxVpnServiceFactory).
public interface IInProcessVpnServiceFactory
{
    IInProcessVpnService Create(string configFolder, IVpnKillSwitch killSwitch);
}
