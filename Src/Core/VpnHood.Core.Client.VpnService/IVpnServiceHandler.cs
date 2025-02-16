using Ga4.Trackers;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device.Adapters;

namespace VpnHood.Core.Client.VpnServicing;

public interface IVpnServiceHandler
{
    ITracker? CreateTracker();
    IVpnAdapter CreateAdapter();
    void ShowNotification(ConnectionInfo connectionInfo);
    void StopNotification();
    void StopSelf();
}