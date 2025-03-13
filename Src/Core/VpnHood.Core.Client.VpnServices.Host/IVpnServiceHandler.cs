using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Host;

public interface IVpnServiceHandler
{
    IVpnAdapter CreateAdapter(VpnAdapterSettings adapterSettings);
    void ShowNotification(ConnectionInfo connectionInfo);
    void StopNotification();
    void StopSelf();
}