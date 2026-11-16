using System.Net.NetworkInformation;

namespace VpnHood.Net.VpnAdapters.Abstractions;

// The OS's network events are static: an adapter's own handlers on them would keep one that was never disposed
// reachable for the process's life, so that no finalizer of it ever ran. The events hold this listener instead,
// which holds the adapter weakly and leaves the events once the adapter is gone.
internal sealed class NetworkChangeListener : IDisposable
{
    private readonly WeakReference<TunVpnAdapter> _adapter;

    public NetworkChangeListener(TunVpnAdapter adapter)
    {
        _adapter = new WeakReference<TunVpnAdapter>(adapter);
        NetworkChange.NetworkAddressChanged += NetworkChange_NetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged += NetworkChange_NetworkAvailabilityChanged;
    }

    private void NetworkChange_NetworkAddressChanged(object? sender, EventArgs e)
    {
        if (_adapter.TryGetTarget(out var adapter))
            adapter.HandleNetworkAddressChanged();
        else
            Dispose();
    }

    private void NetworkChange_NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (_adapter.TryGetTarget(out var adapter))
            adapter.HandleNetworkAvailabilityChanged();
        else
            Dispose();
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= NetworkChange_NetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged -= NetworkChange_NetworkAvailabilityChanged;
    }
}
