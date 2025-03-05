using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.VpnAdapters.Abstractions;
using VpnHood.Core.VpnAdapters.LinuxTun.LinuxNative;

namespace VpnHood.Core.VpnAdapters.LinuxTun;
public class LinuxTunVpnAdapter(LinuxTunVpnAdapterOptions tunAdapterOptions)
    : TunVpnAdapter(tunAdapterOptions)
{
    private int _tunAdapterFd;
    private int? _metric;
    private string? _primaryAdapterName;
    public override bool IsNatSupported => true;

    private static async Task<string> GetPrimaryAdapterName(CancellationToken cancellationToken)
    {
        var mainInterface = await ExecuteCommandAsync("ip route | grep default | awk '{print $5}'", cancellationToken).VhConfigureAwait();
        mainInterface = mainInterface.Trim();
        if (string.IsNullOrEmpty(mainInterface))
            throw new InvalidOperationException("No active network interface found.");

        return mainInterface;
    }

    protected override async Task OpenAdapter(CancellationToken cancellationToken)
    {
        // Get the primary adapter name
        Logger.LogDebug("Getting the primary adapter name...");
        _primaryAdapterName = await GetPrimaryAdapterName(cancellationToken);
        Logger.LogDebug("Primary adapter name is {PrimaryAdapterName}", _primaryAdapterName);

        // delete existing tun interface
        Logger.LogDebug("Clean previous tun adapter...");
        CloseAdapter();

        // Create and configure tun interface
        Logger.LogDebug("Creating tun adapter...");
        await ExecuteCommandAsync($"ip tuntap add dev {AdapterName} mode tun", cancellationToken).VhConfigureAwait();

        // Enable IP forwarding
        Logger.LogDebug("Enabling IP forwarding...");
        await ExecuteCommandAsync("sysctl -w net.ipv4.ip_forward=1", cancellationToken).VhConfigureAwait();
        await ExecuteCommandAsync("sysctl -w net.ipv6.conf.all.forwarding=1", cancellationToken).VhConfigureAwait();

        // Bring up the interface
        Logger.LogDebug("Bringing up the TUN...");
        await ExecuteCommandAsync($"ip link set {AdapterName} up", cancellationToken).VhConfigureAwait();

        // Open TUN Adapter
        Logger.LogDebug("Opening the TUN adapter...");
        _tunAdapterFd = OpenTunAdapter(AdapterName, false);
    }

    protected override async Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        // remove old NAT rule if any
        TryRemoveNat(ipNetwork);

        // Configure NAT with iptables
        var iptables = ipNetwork.IsV4 ? "iptables" : "ip6tables";
        await ExecuteCommandAsync($"{iptables} -t nat -A POSTROUTING -s {ipNetwork} -o {_primaryAdapterName} -j MASQUERADE",
                cancellationToken).VhConfigureAwait();
    }

    private void TryRemoveNat(IpNetwork ipNetwork)
    {
        // Remove NAT rule. try until no rule found
        var res = "ok";
        while (!string.IsNullOrEmpty(res)) {
            var iptables = ipNetwork.IsV4 ? "iptables" : "ip6tables";
            res = VhUtils.TryInvoke("Remove NAT rule", () =>
                ExecuteCommand($"{iptables} -t nat -D POSTROUTING -s {ipNetwork} -o {_primaryAdapterName} -j MASQUERADE"));
        }
    }

    protected override async Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        await ExecuteCommandAsync($"ip addr add {ipNetwork} dev {AdapterName}",
            cancellationToken).VhConfigureAwait();
    }

    protected override async Task AddRoute(IpNetwork ipNetwork, IPAddress gatewayIp, CancellationToken cancellationToken)
    {
        var command = ipNetwork.IsV4
            ? $"ip route add {ipNetwork} dev {AdapterName} via {gatewayIp}"
            : $"ip -6 route add {ipNetwork} dev {AdapterName} via {gatewayIp}";

        if (_metric != null)
            command += $" metric {_metric}";

        await ExecuteCommandAsync(command, cancellationToken).VhConfigureAwait();
    }

    protected override Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        _metric = metric;
        return Task.CompletedTask;
    }
    protected override async Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        var command = $"ip link set dev {AdapterName} mtu {mtu}";
        await ExecuteCommandAsync(command, cancellationToken).VhConfigureAwait();
    }

    protected override async Task SetDnsServers(IPAddress[] ipAddresses, CancellationToken cancellationToken)
    {
        var allDns = string.Join(" ", ipAddresses.Select(x => x.ToString()));
        var command = $"resolvectl dns {AdapterName} {allDns}";
        await ExecuteCommandAsync(command, cancellationToken).VhConfigureAwait();
        await ExecuteCommandAsync($"resolvectl domain {AdapterName} \"~.\"", cancellationToken).VhConfigureAwait();
    }

    private bool IsTunAdapterExists()
    {
        return NetworkInterface
            .GetAllNetworkInterfaces()
            .Any(x => x.Name.Equals(AdapterName, StringComparison.OrdinalIgnoreCase));
    }

    protected override void CloseAdapter()
    {
        // Close the TUN adapter if it is open
        if (_tunAdapterFd != 0) {
            LinuxAPI.close(_tunAdapterFd);
            _tunAdapterFd = 0;
        }

        // Remove existing tun interface
        if (IsTunAdapterExists()) {
            Logger.LogDebug("Removing existing {AdapterName} TUN adapter (if any)...", AdapterName);
            VhUtils.TryInvoke($"remove existing {AdapterName} TUN adapter", () =>
                ExecuteCommand($"ip link delete {AdapterName}"));
        }

        // Remove previous NAT iptables record
        if (UseNat) {
            Logger.LogDebug("Removing previous NAT iptables record for {AdapterName} TUN adapter...", AdapterName);
            if (AdapterIpNetworkV4 != null)
                TryRemoveNat(AdapterIpNetworkV4);

            if (AdapterIpNetworkV6 != null)
                TryRemoveNat(AdapterIpNetworkV6);
        }
    }

    protected override void WaitForTunRead()
    {
        WaitForTun(PollEvent.In);
    }
    protected override void WaitForTunWrite()
    {
        WaitForTun(PollEvent.Out);
    }

    private void WaitForTun(PollEvent pollEvent)
    {
        var pollFd = new PollFD {
            fd = _tunAdapterFd,
            events = (short)pollEvent
        };

        while (true) {
            var result = LinuxAPI.poll([pollFd], 1, -1); // Blocks until data arrives
            if (result >= 0)
                break; // Success, exit loop

            var errorCode = Marshal.GetLastWin32Error();
            if (errorCode == LinuxAPI.EINTR)
                continue; // Poll was interrupted, retry

            throw new PInvokeException("Failed to poll the TUN device for new data.", errorCode);
        }
    }
    protected override bool WritePacket(IPPacket ipPacket)
    {
        var packetBytes = ipPacket.Bytes;

        // Write the packet to the TUN device
        var offset = 0;
        while (offset < packetBytes.Length) {
            var bytesWritten = LinuxAPI.write(_tunAdapterFd, packetBytes, packetBytes.Length - offset);
            if (bytesWritten > 0) {
                offset += bytesWritten; // Advance buffer
                continue;
            }

            var errorCode = Marshal.GetLastWin32Error();
            switch (errorCode) {
                // Buffer full, wait
                case LinuxAPI.EAGAIN:
                    if (offset > 0)
                        throw new SystemException("Partial write to TUN device. System in unstable");
                    return false;

                // Interrupted, retry
                case LinuxAPI.EINTR:
                    continue;

                default:
                    throw new PInvokeException("Could not write to TUN.", errorCode);
            }
        }

        return true;
    }

    protected override void ReadPackets(List<IPPacket> packetList, int mtu)
    {
        while (Started && packetList.Count < packetList.Capacity) {
            var buffer = new byte[mtu];
            var bytesRead = LinuxAPI.read(_tunAdapterFd, buffer, buffer.Length);

            // check for errors
            if (bytesRead <= 0) {
                var errorCode = Marshal.GetLastWin32Error();
                switch (errorCode) {
                    // No data available, wait
                    case LinuxAPI.EAGAIN: return;

                    // Interrupted, retry
                    case LinuxAPI.EINTR:
                        continue;

                    default:
                        throw new PInvokeException("Could not read from TUN.", errorCode);
                }
            }

            // Parse the packet and add to the list
            var ipPacket = Packet.ParsePacket(LinkLayers.Raw, buffer).Extract<IPPacket>();
            packetList.Add(ipPacket);
        }
    }

    private static int OpenTunAdapter(string adapterName, bool blockingMode)
    {
        // Open the TUN device file
        var tunDeviceFd = LinuxAPI.open("/dev/net/tun", LinuxAPI.ORdwr);
        if (tunDeviceFd < 0)
            throw new InvalidOperationException("Failed to open TUN device.");

        // Configure the device
        var ifr = new Ifreq {
            ifr_name = adapterName,
            ifr_flags = LinuxAPI.IFF_TUN | LinuxAPI.IFF_NO_PI
        };

        var ioctlResult = LinuxAPI.ioctl(tunDeviceFd, LinuxAPI.TUNSETIFF, ref ifr);
        if (ioctlResult < 0) {
            LinuxAPI.close(tunDeviceFd);
            throw new PInvokeException($"Failed to configure TUN device. IoctlResult: {ioctlResult}");
        }

        if (!blockingMode) {
            if (LinuxAPI.fcntl(tunDeviceFd, LinuxAPI.F_SETFL, LinuxAPI.O_NONBLOCK) < 0) {
                LinuxAPI.close(tunDeviceFd);
                throw new PInvokeException("Failed to set TUN device to non-blocking mode.");
            }
        }

        return tunDeviceFd;
    }

    private static string ExecuteCommand(string command)
    {
        return OsUtils.ExecuteCommand("/bin/bash", $"-c \"{command}\"");
    }

    private static Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
    {
        return OsUtils.ExecuteCommandAsync("/bin/bash", $"-c \"{command}\"", cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // The adapter is an unmanaged resource; it must be closed if it is open
        if (_tunAdapterFd != 0)
            CloseAdapter();
    }
}