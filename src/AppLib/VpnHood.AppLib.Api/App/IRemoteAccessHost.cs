namespace VpnHood.AppLib.Api.App;

// Pairing, from the API's side. The API answers three calls about it and owns none of it: what
// listens for a phone is a web host, which the head hands in through AppOptions. A head with no
// pairing screen hands in nothing, IsRemoteAccessSupported is false and those three calls throw -
// which is what keeps this half free of a listener, and the dependency pointing one way only.
public interface IRemoteAccessHost
{
    Task<RemoteAccessState> RefreshRemoteAccess(CancellationToken cancellationToken);
    Task<RemoteAccessState> StartRemoteAccess(CancellationToken cancellationToken);
    Task StopRemoteAccess(CancellationToken cancellationToken);
}
