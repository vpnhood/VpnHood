using VpnHood.Core.Client.Devices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;

namespace VpnHood.Core.Client.Devices.Linux;

// The VPN service in the app's own process, which a head hands its LinuxDevice for a debugger's run (dev). Its lines go
// to the app's log: the VPN host's log is its own process's.
public sealed class LinuxVpnServiceFactory : IInProcessVpnServiceFactory
{
    public IInProcessVpnService Create(string configFolder, IVpnKillSwitch killSwitch)
    {
        return new LinuxVpnService(configFolder, killSwitch, withLogger: false);
    }
}
