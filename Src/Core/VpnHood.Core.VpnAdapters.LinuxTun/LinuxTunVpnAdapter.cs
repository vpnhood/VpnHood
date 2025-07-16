using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.VpnAdapters.Abstractions;
using VpnHood.Core.VpnAdapters.LinuxTun.LinuxNative;

namespace VpnHood.Core.VpnAdapters.LinuxTun;
public class LinuxTunVpnAdapter(LinuxVpnAdapterSettings adapterSettings)
    : TunVpnAdapter(adapterSettings)
{
    private int _tunAdapterFd;
    private int? _metric;
    private string? _primaryAdapterName;
    private StructPollfd[]? _pollFdReads;
    private StructPollfd[]? _pollFdWrites;
    private readonly byte[] _writeBuffer = new byte[0xFFFF];
    protected override bool IsSocketProtectedByBind => true;
    public override bool CanProtectSocket => !string.IsNullOrEmpty(_primaryAdapterName);

    public override bool IsNatSupported => true;
    public override bool IsAppFilterSupported => false;
    protected override string? AppPackageId => null;
    protected override Task SetAllowedApps(string[] packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("App filtering is not supported on LinuxTun.");

    protected override Task SetDisallowedApps(string[] packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("App filtering is not supported on LinuxTun.");

    private static async Task<string> GetPrimaryAdapterName(CancellationToken cancellationToken)
    {
        var mainInterface = await ExecuteCommandAsync("ip route | grep default | awk '{print $5}'", cancellationToken).Vhc();
        mainInterface = mainInterface.Trim();
        if (string.IsNullOrEmpty(mainInterface))
            throw new InvalidOperationException("No active network interface found.");

        return mainInterface;
    }

    public static string? FindInterfaceNameForIp(IPAddress ip)
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();
        
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var networkInterface in interfaces) {
            var props = networkInterface.GetIPProperties();
            var addresses = props.UnicastAddresses;

            if (addresses.Any(ua => ua.Address.Equals(ip))) {
                return networkInterface.Name; // on Linux, this will be "eth0", "tun0", etc.
            }
        }

        return null; // not found
    }

    protected override async Task AdapterAdd(CancellationToken cancellationToken)
    {
        // Get the primary adapter name
        VhLogger.Instance.LogDebug("Getting the primary adapter name...");
        _primaryAdapterName = await GetPrimaryAdapterName(cancellationToken);
        VhLogger.Instance.LogDebug("Primary adapter name is {PrimaryAdapterName}", _primaryAdapterName);

        // delete existing tun interface
        VhLogger.Instance.LogDebug("Clean previous tun adapter...");
        AdapterRemove();

        // Create and configure tun interface
        VhLogger.Instance.LogDebug("Creating tun adapter...");
        await ExecuteCommandAsync($"ip tuntap add dev {AdapterName} mode tun", cancellationToken).Vhc();

        // Enable IP forwarding
        VhLogger.Instance.LogDebug("Enabling IP forwarding...");
        await ExecuteCommandAsync("sysctl -w net.ipv4.ip_forward=1", cancellationToken).Vhc();
        await ExecuteCommandAsync("sysctl -w net.ipv6.conf.all.forwarding=1", cancellationToken).Vhc();

        // Bring up the interface
        VhLogger.Instance.LogDebug("Bringing up the TUN...");
        await ExecuteCommandAsync($"ip link set {AdapterName} up", cancellationToken).Vhc();
    }

    protected override void AdapterRemove()
    {
        // close if open
        AdapterClose();

        var tunAdapterExists = NetworkInterface
            .GetAllNetworkInterfaces()
            .Any(x => x.Name.Equals(AdapterName, StringComparison.OrdinalIgnoreCase));

        // Remove existing tun interface
        if (tunAdapterExists) {
            VhLogger.Instance.LogDebug("Removing existing {AdapterName} TUN adapter (if any)...", AdapterName);
            VhUtils.TryInvoke($"remove existing {AdapterName} TUN adapter", () =>
                ExecuteCommand($"ip link delete {AdapterName}"));
        }

        // Remove previous NAT iptables record
        if (UseNat) {
            VhLogger.Instance.LogDebug("Removing previous NAT iptables record for {AdapterName} TUN adapter...", AdapterName);
            if (AdapterIpNetworkV4 != null)
                TryRemoveNat(AdapterIpNetworkV4);

            if (AdapterIpNetworkV6 != null)
                TryRemoveNat(AdapterIpNetworkV6);
        }
    }

    protected override Task AdapterOpen(CancellationToken cancellationToken)
    {
        // Open TUN Adapter
        VhLogger.Instance.LogDebug("Opening the TUN adapter...");
        _tunAdapterFd = OpenTunAdapter(AdapterName, false);
        _pollFdReads = [new StructPollfd {
            Fd = _tunAdapterFd, Events = OsConstants.Pollin
        }];
        _pollFdWrites = [new StructPollfd {
            Fd = _tunAdapterFd, Events = OsConstants.Pollout }];

        return Task.CompletedTask;
    }

    protected override void AdapterClose()
    {
        // Close the TUN adapter if it is open
        if (_tunAdapterFd != 0) {
            LinuxAPI.close(_tunAdapterFd);
            _tunAdapterFd = 0;
        }
    }

    protected override async Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        // remove old NAT rule if any
        TryRemoveNat(ipNetwork);

        // Configure NAT with iptables
        var iptables = ipNetwork.IsV4 ? "iptables" : "ip6tables";
        await ExecuteCommandAsync($"{iptables} -t nat -A POSTROUTING -s {ipNetwork} -o {_primaryAdapterName} -j MASQUERADE",
                cancellationToken).Vhc();

        // sudo iptables 
        await ExecuteCommandAsync($"{iptables} -A FORWARD -i {AdapterName} -o {_primaryAdapterName} -j ACCEPT",
            cancellationToken).Vhc();

        // sudo iptables 
        await ExecuteCommandAsync($"{iptables} -A FORWARD -i {_primaryAdapterName} -o {AdapterName} -m state --state RELATED,ESTABLISHED -j ACCEPT",
            cancellationToken).Vhc();
    }

    private void TryRemoveNat(IpNetwork ipNetwork)
    {
        var iptables = ipNetwork.IsV4 ? "iptables" : "ip6tables";

        // Remove NAT rule. try until no rule found
        var res = "ok";
        while (!string.IsNullOrEmpty(res)) {
            res = VhUtils.TryInvoke("Remove NAT rule", () =>
                ExecuteCommand($"{iptables} -t nat -D POSTROUTING -s {ipNetwork} -o {_primaryAdapterName} -j MASQUERADE"));
        }

        // Remove forwarding rules
        VhUtils.TryInvoke("Remove NAT forwarding rules...", () =>
            ExecuteCommand($"{iptables}-save | grep -v -w \"{AdapterName}\" | {iptables}-restore"));
    }

    protected override async Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        await ExecuteCommandAsync($"ip addr add {ipNetwork} dev {AdapterName}",
            cancellationToken).Vhc();
    }

    protected override async Task AddRoute(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        var command = ipNetwork.IsV4
            ? $"ip route add {ipNetwork} dev {AdapterName}"
            : $"ip -6 route add {ipNetwork} dev {AdapterName}";

        if (_metric != null)
            command += $" metric {_metric}";

        await ExecuteCommandAsync(command, cancellationToken).Vhc();
    }

    protected override Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        _metric = metric;
        return Task.CompletedTask;
    }

    protected override Task SetSessionName(string sessionName, CancellationToken cancellationToken)
    {
        // Not supported. Ignore
        return Task.CompletedTask;
    }

    protected override async Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        var command = $"ip link set dev {AdapterName} mtu {mtu}";
        await ExecuteCommandAsync(command, cancellationToken).Vhc();
    }

    protected override async Task SetDnsServers(IPAddress[] dnsServers, CancellationToken cancellationToken)
    {
        var allDns = string.Join(" ", dnsServers.Select(x => x.ToString()));
        var command = $"resolvectl dns {AdapterName} {allDns}";
        await ExecuteCommandAsync(command, cancellationToken).Vhc();
        await ExecuteCommandAsync($"resolvectl domain {AdapterName} \"~.\"", cancellationToken).Vhc();
    }

    protected override void WaitForTunRead()
    {
        if (_pollFdReads != null)
            WaitForTun(_pollFdReads);
    }
    protected override void WaitForTunWrite()
    {
        if (_pollFdWrites != null)
            WaitForTun(_pollFdWrites);
    }


    private static void WaitForTun(StructPollfd[] pollFds)
    {
        while (true) {
            var result = LinuxAPI.poll(pollFds, 1, -1);
            if (result >= 0)
                break; // Success, exit loop

            var errorCode = Marshal.GetLastWin32Error();
            if (errorCode == OsConstants.Eintr)
                continue; // Poll was interrupted, retry

            throw new PInvokeException("Failed to poll the TUN device for new data.", errorCode);
        }
    }

    protected override bool WritePacket(IpPacket ipPacket)
    {
        var packetBytes = ipPacket.GetUnderlyingBufferUnsafe(_writeBuffer, out var bufferLength);

        // Write the packet to the TUN device
        var offset = 0;
        while (offset < bufferLength) {
            var bytesWritten = LinuxAPI.write(_tunAdapterFd, packetBytes, bufferLength - offset);
            if (bytesWritten > 0) {
                offset += bytesWritten; // Advance offset
                packetBytes = packetBytes[bytesWritten..]; // Advance buffer (rare case)
                continue;
            }

            var errorCode = Marshal.GetLastWin32Error();
            switch (errorCode) {
                // Buffer full, wait
                case OsConstants.Eagain:
                    if (offset > 0)
                        throw new SystemException("Partial write to TUN device. System in unstable");
                    return false;

                // Interrupted, retry
                case OsConstants.Eintr:
                    continue;

                default:
                    throw new PInvokeException("Could not write to TUN.", errorCode);
            }
        }

        return true;
    }

    protected override bool ReadPacket(byte[] buffer)
    {
        var bytesRead = LinuxAPI.read(_tunAdapterFd, buffer, buffer.Length);
        if (bytesRead > 0)
            return true;

        // check for errors
        var errorCode = Marshal.GetLastWin32Error();
        return errorCode switch {
            // No data available, wait
            OsConstants.Eagain => false,
            // Interrupted, retry
            OsConstants.Eintr => throw new IOException("Read from TUN was interrupted. Retrying..."),
            _ => throw new PInvokeException("Could not read from TUN.", errorCode)
        };
    }

    private static int OpenTunAdapter(string adapterName, bool blockingMode)
    {
        // Open the TUN device file
        var tunDeviceFd = LinuxAPI.open("/dev/net/tun", OsConstants.ORdwr);
        if (tunDeviceFd < 0)
            throw new InvalidOperationException("Failed to open TUN device.");

        // Configure the device
        var ifr = new Ifreq {
            ifr_name = adapterName,
            ifr_flags = (short)(InterfaceFlag.IffTun | InterfaceFlag.IffNoPi)
        };

        var ioctlResult = LinuxAPI.ioctl(tunDeviceFd, OsConstants.Tunsetiff, ref ifr);
        if (ioctlResult < 0) {
            LinuxAPI.close(tunDeviceFd);
            throw new PInvokeException($"Failed to configure TUN device. IoctlResult: {ioctlResult}");
        }

        if (!blockingMode) {
            if (LinuxAPI.fcntl(tunDeviceFd, OsConstants.FSetfl, OsConstants.ONonblock) < 0) {
                LinuxAPI.close(tunDeviceFd);
                throw new PInvokeException("Failed to set TUN device to non-blocking mode.");
            }
        }

        return tunDeviceFd;
    }

    protected override void BindSocketToIp(Socket socket, IPAddress ipAddress)
    {
        var adapterName = FindInterfaceNameForIp(ipAddress);
        if (string.IsNullOrEmpty(adapterName))
            throw new InvalidOperationException($"No network interface found for IP address {ipAddress}.");
        
        var optVal = Encoding.ASCII.GetBytes(adapterName + "\0");
        var result = LinuxAPI.setsockopt((int)socket.Handle, level: OsConstants.SolSocket,
            OsConstants.SoBindtodevice, optVal, (uint)optVal.Length);

        if (result < 0)
            throw new PInvokeException($"Failed to bind socket to device {adapterName}.");
    }

    private static string ExecuteCommand(string command)
    {
        return OsUtils.ExecuteCommand("/bin/bash", $"-c \"{command}\"");
    }

    private static Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
    {
        return OsUtils.ExecuteCommandAsync("/bin/bash", $"-c \"{command}\"", cancellationToken);
    }

    protected override void DisposeUnmanaged()
    {
        if (_tunAdapterFd != 0)
            AdapterRemove();

        base.DisposeUnmanaged();
    }

    ~LinuxTunVpnAdapter()
    {
        Dispose(false);
    }
}