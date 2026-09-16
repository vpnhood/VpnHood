using VpnHood.AppLib.Api.App;

namespace VpnHood.AppLib.Api.InProcessHost;

// Pairing, from the API's side. The API answers three calls about it and owns none of it: what
// listens for a phone is a web host, which the head brings up and hands in here. A head with no
// pairing screen hands in nothing, and those three calls say the feature is not there - which is
// what keeps this half free of a listener, and the dependency pointing one way only.
public interface IRemoteAccessHost
{
    Task<RemoteAccessState> RefreshRemoteAccess();
    Task<RemoteAccessState> StartRemoteAccess();
    void StopRemoteAccess();
}
