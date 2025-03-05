using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.VpnAdapters.Abstractions;

public abstract class TunVpnAdapter(TunVpnAdapterOptions tunAdapterOptions) : IVpnAdapter
{
    private readonly int _maxPacketSendDelayMs = (int)tunAdapterOptions.MaxPacketSendDelay.TotalMilliseconds;
    private int _mtu = 0xFFFF;
    private readonly int _maxAutoRestartCount = tunAdapterOptions.MaxAutoRestartCount;
    private int _autoRestartCount;

    protected bool IsDisposed { get; private set; }
    protected ILogger Logger { get; } = tunAdapterOptions.Logger;
    protected bool UseNat { get; private set; }
    protected abstract Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken);
    protected abstract Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken);
    protected abstract Task SetDnsServers(IPAddress[] ipAddresses, CancellationToken cancellationToken);
    protected abstract Task AddRoute(IpNetwork ipNetwork, IPAddress gatewayIp, CancellationToken cancellationToken);
    protected abstract Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken);
    protected abstract Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken);

    public virtual void ProtectSocket(Socket socket)
    {
    } //todo

    protected abstract void CloseAdapter();
    protected abstract void WaitForTunWrite();
    protected abstract void WaitForTunRead();
    protected abstract void ReadPackets(List<IPPacket> packetList, int mtu);
    protected abstract bool WritePacket(IPPacket ipPacket);

    public event EventHandler<PacketReceivedEventArgs>? PacketReceivedFromInbound;
    public event EventHandler? Disposed;
    public string AdapterName { get; } = tunAdapterOptions.AdapterName;
    public bool CanSendPacketToOutbound => UseNat;
    public IPAddress? PrimaryAdapterIpV4 { get; private set; }
    public IPAddress? PrimaryAdapterIpV6 { get; private set; }
    public IpNetwork? AdapterIpNetworkV4 { get; private set; }
    public IpNetwork? AdapterIpNetworkV6 { get; private set; }
    public IPAddress? GatewayIpV4 { get; private set; }
    public IPAddress? GatewayIpV6 { get; private set; }
    public bool Started { get; private set; }
    public virtual bool IsDnsServersSupported => true;
    public abstract bool IsNatSupported { get; }
    public bool CanProtectSocket => true; //todo

    public async Task Start(VpnAdapterOptions options, CancellationToken cancellationToken)
    {
        if (IsDisposed)
            throw new ObjectDisposedException(GetType().Name);

        if (UseNat && !IsNatSupported)
            throw new NotSupportedException("NAT is not supported by this adapter.");

        Logger.LogInformation("Starting {AdapterName} adapter.", AdapterName);

        // get the WAN adapter IP
        PrimaryAdapterIpV4 =
            GetPrimaryAdapterIp(new IPEndPoint(IPAddressUtil.GoogleDnsServers.First(x => x.IsV4()), 53));
        PrimaryAdapterIpV6 =
            GetPrimaryAdapterIp(new IPEndPoint(IPAddressUtil.GoogleDnsServers.First(x => x.IsV6()), 53));
        AdapterIpNetworkV4 = options.VirtualIpNetworkV4;
        AdapterIpNetworkV6 = options.VirtualIpNetworkV6;
        UseNat = options.UseNat;
        _mtu = options.Mtu ?? _mtu;


        try {
            // create tun adapter
            Logger.LogInformation("Initializing TUN adapter...");
            await OpenAdapter(cancellationToken).VhConfigureAwait();

            // Private IP Networks
            Logger.LogDebug("Adding private networks...");
            if (options.VirtualIpNetworkV4 != null) {
                GatewayIpV4 = BuildGatewayFromFromNetwork(options.VirtualIpNetworkV4);
                await AddAddress(options.VirtualIpNetworkV4, cancellationToken).VhConfigureAwait();
            }

            if (options.VirtualIpNetworkV6 != null) {
                GatewayIpV6 = BuildGatewayFromFromNetwork(options.VirtualIpNetworkV6);
                await AddAddress(options.VirtualIpNetworkV6, cancellationToken).VhConfigureAwait();
            }

            // set metric
            Logger.LogDebug("Setting metric...");
            if (options.Metric != null) {
                await SetMetric(options.Metric.Value,
                    ipV4: options.VirtualIpNetworkV4 != null,
                    ipV6: options.VirtualIpNetworkV6 != null,
                    cancellationToken).VhConfigureAwait();
            }

            // set mtu
            if (options.Mtu != null) {
                Logger.LogDebug("Setting MTU...");
                await SetMtu(options.Mtu.Value,
                    ipV4: options.VirtualIpNetworkV4 != null,
                    ipV6: options.VirtualIpNetworkV6 != null,
                    cancellationToken).VhConfigureAwait();
            }

            // set DNS servers
            Logger.LogDebug("Setting DNS servers...");
            await SetDnsServers(options.DnsServers, cancellationToken).VhConfigureAwait();

            // add routes
            Logger.LogDebug("Adding routes...");
            foreach (var network in options.IncludeNetworks) {
                var gateway = network.IsV4 ? GatewayIpV4 : GatewayIpV6;
                if (gateway != null)
                    await AddRoute(network, gateway, cancellationToken).VhConfigureAwait();
            }

            // add NAT
            if (UseNat) {
                Logger.LogDebug("Adding NAT...");
                if (options.VirtualIpNetworkV4 != null && PrimaryAdapterIpV4 != null)
                    await AddNat(options.VirtualIpNetworkV4, cancellationToken).VhConfigureAwait();

                if (options.VirtualIpNetworkV6 != null && PrimaryAdapterIpV4 != null)
                    await AddNat(options.VirtualIpNetworkV6, cancellationToken).VhConfigureAwait();
            }

            // start reading packets
            _ = Task.Run(ReadingPacketTask, CancellationToken.None);

            Started = true;
            Logger.LogInformation("TUN adapter started.");
        }
        catch (ExternalException ex) {
            Logger.LogError(ex, "Failed to start TUN adapter.");
            Stop();
            throw;
        }
    }

    protected abstract Task OpenAdapter(CancellationToken cancellationToken);

    public void Stop()
    {
        if (Started)
            return;

        Logger.LogInformation("Stopping {AdapterName} adapter.", AdapterName);
        CloseAdapter();
        Logger.LogInformation("TUN adapter stopped.");
        Started = false;
    }

    public virtual UdpClient CreateProtectedUdpClient(int port, AddressFamily addressFamily)
    {
        var udpClient = addressFamily switch {
            AddressFamily.InterNetwork when PrimaryAdapterIpV4 != null => new UdpClient(
                new IPEndPoint(PrimaryAdapterIpV4, port)),
            AddressFamily.InterNetworkV6 when PrimaryAdapterIpV6 != null => new UdpClient(
                new IPEndPoint(PrimaryAdapterIpV6, port)),
            _ => new UdpClient(port, addressFamily)
        };

        ProtectSocket(udpClient.Client);
        return udpClient;
    }

    public virtual TcpClient CreateProtectedTcpClient(int port, AddressFamily addressFamily)
    {
        var tcpClient = addressFamily switch {
            AddressFamily.InterNetwork when PrimaryAdapterIpV4 != null => new TcpClient(
                new IPEndPoint(PrimaryAdapterIpV4, port)),
            AddressFamily.InterNetworkV6 when PrimaryAdapterIpV6 != null => new TcpClient(
                new IPEndPoint(PrimaryAdapterIpV6, port)),
            _ => throw new InvalidOperationException(
                "Could not create a protected TCP client because the primary adapter IP is not available.")
        };

        ProtectSocket(tcpClient.Client);
        return tcpClient;
    }

    private IPAddress? GetPrimaryAdapterIp(IPEndPoint remoteEndPoint)
    {
        try {
            using var udpClient = new UdpClient();
            udpClient.Connect(remoteEndPoint);
            return (udpClient.Client.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (Exception) {
            Logger.LogDebug("Failed to get primary adapter IP. RemoteEndPoint: {RemoteEndPoint}",
                remoteEndPoint);
            return null;
        }
    }

    private static IPAddress? BuildGatewayFromFromNetwork(IpNetwork ipNetwork)
    {
        // Check for small subnets (IPv4: /31, /32 | IPv6: /128)
        return ipNetwork is { IsV4: true, PrefixLength: >= 31 } or { IsV6: true, PrefixLength: 128 }
            ? null
            : IPAddressUtil.Increment(ipNetwork.FirstIpAddress);
    }

    public void SendPacketToInbound(IPPacket ipPacket)
    {
        SendPacket(ipPacket); //todo
    }

    public void SendPacketToInbound(IList<IPPacket> packets)
    {
        SendPacket(packets); //todo
    }

    public void SendPacketToOutbound(IPPacket ipPacket)
    {
        SendPacket(ipPacket); //todo
    }

    public void SendPacketToOutbound(IList<IPPacket> ipPackets)
    {
        SendPacket(ipPackets); //todo
    }

    public void SendPacket(IPPacket ipPacket)
    {
        if (!Started)
            throw new InvalidOperationException("TUN adapter is not started.");

        try {
            SendPacketInternal(ipPacket);
            _autoRestartCount = 0;
        }
        catch (Exception ex) {
            if (_autoRestartCount <= _maxAutoRestartCount) {
                Logger.LogError(ex, "Failed to send packet via TUN adapter.");
                RestartAdapter();
            }
        }
    }

    private void SendPacketInternal(IPPacket ipPacket)
    {
        // try to send the packet with exponential backoff
        var sent = false;
        var delay = 5;
        while (true) {
            // break if the packet is sent
            if (WritePacket(ipPacket)) {
                sent = true;
                break;
            }

            // break if delay exceeds the max delay
            if (delay > _maxPacketSendDelayMs)
                break;

            // wait for the next try
            delay *= 2;
            Task.Delay(delay).Wait();
            WaitForTunWrite();
        }

        // log if failed to send
        if (!sent)
            Logger.LogWarning("Failed to send packet via WinTun adapter.");
    }

    public void SendPacket(IList<IPPacket> packets)
    {
        foreach (var packet in packets)
            SendPacket(packet);
    }

    private readonly SemaphoreSlim _sendPacketSemaphore = new(1, 1);

    public Task SendPacketAsync(IPPacket ipPacket)
    {
        return _sendPacketSemaphore.WaitAsync().ContinueWith(_ => {
            try {
                SendPacket(ipPacket);
            }
            finally {
                _sendPacketSemaphore.Release();
            }
        });
    }

    public async Task SendPacketAsync(IList<IPPacket> packets)
    {
        foreach (var packet in packets)
            await SendPacketAsync(packet).VhConfigureAwait();
    }

    private void ReadingPacketTask()
    {
        var packetList = new List<IPPacket>(tunAdapterOptions.MaxPacketCount);

        // Read packets from TUN adapter
        while (Started && !IsDisposed) {
            try {
                ReadPackets(packetList, _mtu);
                InvokeReadPackets(packetList);
                WaitForTunRead();
                _autoRestartCount = 0;
            }
            catch (Exception ex) {
                Logger.LogError(ex, "Error in reading packets from TUN adapter.");
                if (_autoRestartCount >= _maxAutoRestartCount)
                    break;
                RestartAdapter();
            }
        }

        Stop();
    }

    private void RestartAdapter()
    {
        _autoRestartCount++;
        Logger.LogWarning("Restarting the adapter. RestartCount: {RestartCount}", _autoRestartCount);
        CloseAdapter();
        OpenAdapter(CancellationToken.None);
    }

    private void InvokeReadPackets(List<IPPacket> packetList)
    {
        try {
            if (packetList.Count > 0)
                PacketReceivedFromInbound?.Invoke(this, new PacketReceivedEventArgs(packetList));
        }
        catch (Exception ex) {
            Logger.LogError(ex, "Error in invoking packet received event.");
        }
        finally {
            packetList.Clear();
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        IsDisposed = true;

        // release managed resources when disposing
        if (disposing) {
            Stop();

            // notify the subscribers that the adapter is disposed
            Disposed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~TunVpnAdapter()
    {
        Dispose(false);
    }
}