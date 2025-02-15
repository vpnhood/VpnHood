using Ga4.Trackers;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device;

namespace VpnHood.Core.Client.Services;

public interface IVpnServiceHandler
{
    ITracker? CreateTracker();
    IVpnAdapter CreateAdapter();
    void ShowNotification(ConnectionInfo connectionInfo);
    void StopNotification();
    void StopSelf();
}