namespace VpnHood.Core.Client.Devices.Abstractions;

// A VPN service in the app's own process (IInProcessVpnServiceFactory). The device connects it at each start, and makes
// another once it has ended with its session.
public interface IInProcessVpnService : IDisposable
{
    bool IsDisposed { get; }
    void OnConnect();
}
