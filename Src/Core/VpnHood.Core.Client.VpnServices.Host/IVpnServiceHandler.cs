using Ga4.Trackers;
using VpnHood.Core.Client.Device.Adapters;
using VpnHood.Core.Client.VpnServices.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Host;

public interface IVpnServiceHandler
{
    ITracker? CreateTracker();
    IVpnAdapter CreateAdapter();
    void ShowNotification(ConnectionInfo connectionInfo);
    void StopNotification();
    void StopSelf();
}