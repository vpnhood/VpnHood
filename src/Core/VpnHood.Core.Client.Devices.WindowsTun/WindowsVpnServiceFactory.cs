using VpnHood.Core.Client.Devices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;

namespace VpnHood.Core.Client.Devices.WindowsTun;

// The VPN service in the app's own process, which a head hands its WindowsDevice for a debugger's run (dev) or a MAUI
// app. Its lines go to the app's log: the VPN host's log is its own process's.
public sealed class WindowsVpnServiceFactory : IInProcessVpnServiceFactory
{
    public IInProcessVpnService Create(string configFolder, IVpnKillSwitch killSwitch)
    {
        return new WindowsVpnService(configFolder, killSwitch, withLogger: false);
    }
}
