using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Packets;
using VpnHood.Net.PacketTransports;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Test.Device;

// A null adapter whose start waits at a gate the test opens, before the adapter's own start begins: what comes between
// a caller's last look and the start, as a close, comes while it waits. The wait does not end with the caller's token,
// as nothing ends it there
public sealed class GatedVpnAdapter : IVpnAdapter
{
    private readonly TestNullVpnAdapter _vpnAdapter = new();
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _startWaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposeWaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposeCallCount;

    // completes once a start waits at the gate
    public Task StartWaiting => _startWaiting.Task;

    // whether the start's token was canceled when the gate opened, as by a close meanwhile
    public bool IsStartCanceled { get; private set; }

    // where a test holds the adapter's close, as a slow one: the first close waits for it first, on its own thread, and
    // a later one goes on, as a client's own close after a revoke's
    public Task? DisposeGate { get; set; }

    // completes once a close waits at its gate
    public Task DisposeWaiting => _disposeWaiting.Task;

    // how many closes have begun
    public int DisposeCallCount => _disposeCallCount;

    // the options of the last start that went on from the gate
    public VpnAdapterOptions? StartOptions { get; private set; }

    public GatedVpnAdapter()
    {
        _vpnAdapter.Started += (_, options) => Started?.Invoke(this, options);
    }

    public void OpenGate() => _gate.TrySetResult();

    // a start's word once more, as a restart's that comes late
    public void RaiseStarted(VpnAdapterOptions options) => Started?.Invoke(this, options);

    public async Task Start(VpnAdapterOptions options, CancellationToken cancellationToken)
    {
        _startWaiting.TrySetResult();
        await _gate.Task;
        IsStartCanceled = cancellationToken.IsCancellationRequested;
        StartOptions = options;
        await _vpnAdapter.Start(options, cancellationToken);
    }

    public event EventHandler? Disposed {
        add => _vpnAdapter.Disposed += value;
        remove => _vpnAdapter.Disposed -= value;
    }

    public event EventHandler? PrimaryAdapterIpChanged {
        add => _vpnAdapter.PrimaryAdapterIpChanged += value;
        remove => _vpnAdapter.PrimaryAdapterIpChanged -= value;
    }

    public event EventHandler? PrimaryNetworkChanged {
        add => _vpnAdapter.PrimaryNetworkChanged += value;
        remove => _vpnAdapter.PrimaryNetworkChanged -= value;
    }

    public event EventHandler<Exception>? Failed {
        add => _vpnAdapter.Failed += value;
        remove => _vpnAdapter.Failed -= value;
    }

    public event EventHandler<VpnAdapterOptions>? Started;

    public event EventHandler<IpPacket>? PacketReceived {
        add => _vpnAdapter.PacketReceived += value;
        remove => _vpnAdapter.PacketReceived -= value;
    }

    public bool IsStarted => _vpnAdapter.IsStarted;
    public bool CanProtectSocket => _vpnAdapter.CanProtectSocket;
    public bool IsSending => _vpnAdapter.IsSending;
    public ReadOnlyPacketTransportStat PacketStat => _vpnAdapter.PacketStat;
    public bool IsNatSupported(IpVersion ipVersion) => _vpnAdapter.IsNatSupported(ipVersion);
    public bool ProtectSocket(Socket socket) => _vpnAdapter.ProtectSocket(socket);
    public bool ProtectSocket(Socket socket, IPAddress ipAddress) => _vpnAdapter.ProtectSocket(socket, ipAddress);
    public IReadOnlyList<IPAddress> GetPrimaryAdapterDnsServers() => _vpnAdapter.GetPrimaryAdapterDnsServers();
    public void Stop() => _vpnAdapter.Stop();
    public IPAddress? GetPrimaryAdapterAddress(IpVersion ipVersion) => _vpnAdapter.GetPrimaryAdapterAddress(ipVersion);
    public bool IsIpVersionSupported(IpVersion ipVersion) => _vpnAdapter.IsIpVersionSupported(ipVersion);
    public void RefreshPrimaryAdapterIps() => _vpnAdapter.RefreshPrimaryAdapterIps();
    public bool SendPacketQueued(IpPacket ipPacket) => _vpnAdapter.SendPacketQueued(ipPacket);
    public ValueTask SendPacketQueuedAsync(IpPacket ipPacket) => _vpnAdapter.SendPacketQueuedAsync(ipPacket);
    public void Dispose()
    {
        if (Interlocked.Increment(ref _disposeCallCount) == 1 && DisposeGate is { } disposeGate) {
            _disposeWaiting.TrySetResult();
            disposeGate.Wait();
        }

        _vpnAdapter.Dispose();
    }
}
