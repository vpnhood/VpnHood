using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.VpnAdapters.Abstractions;
using VpnHood.Core.VpnAdapters.LinuxTun.LinuxNative;

namespace VpnHood.Core.VpnAdapters.LinuxTun;

public class LinuxTunVpnAdapter(LinuxVpnAdapterSettings adapterSettings)
    : TunVpnAdapter(adapterSettings)
{
    // -1 (not 0) is the "closed" sentinel: fd 0 is a valid descriptor (stdin), so using it would let a
    // post-close read/write hit an unrelated descriptor instead of failing.
    private const int InvalidFd = -1;
    private int _tunAdapterFd = InvalidFd;
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
    protected override bool RestartAfterNetworkAddressChanged => true;

    protected override Task SetAllowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("App filtering is not supported on LinuxTun.");

    protected override Task SetDisallowedApps(IEnumerable<string> packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("App filtering is not supported on LinuxTun.");

    private static async Task<string> GetPrimaryAdapterName(CancellationToken cancellationToken)
    {
        var mainInterface = await ExecuteCommandAsync("ip route | grep default | awk '{print $5}'", cancellationToken).Vhc();
        mainInterface = mainInterface.Split("\n").FirstOrDefault()?.Trim();
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
            VhLogger.Instance.LogDebug("Removing previous NAT iptables record for {AdapterName} TUN adapter...",
                AdapterName);
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
        _pollFdReads = [
            new StructPollfd {
                Fd = _tunAdapterFd, Events = OsConstants.Pollin
            }
        ];
        _pollFdWrites = [
            new StructPollfd {
                Fd = _tunAdapterFd, Events = OsConstants.Pollout
            }
        ];

        return Task.CompletedTask;
    }

    protected override void AdapterClose()
    {
        // Close the TUN adapter if it is open
        var fd = Interlocked.Exchange(ref _tunAdapterFd, InvalidFd);
        if (fd != InvalidFd)
            LinuxAPI.close(fd);
    }

    protected override async Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        // remove old NAT rule if any
        TryRemoveNat(ipNetwork);

        // Configure NAT with iptables
        var iptables = ipNetwork.IsV4 ? "iptables" : "ip6tables";
        await ExecuteCommandAsync(
            $"{iptables} -t nat -A POSTROUTING -s {ipNetwork} -o {_primaryAdapterName} -j MASQUERADE",
            cancellationToken).Vhc();

        // sudo iptables 
        await ExecuteCommandAsync($"{iptables} -A FORWARD -i {AdapterName} -o {_primaryAdapterName} -j ACCEPT",
            cancellationToken).Vhc();

        // sudo iptables 
        await ExecuteCommandAsync(
            $"{iptables} -A FORWARD -i {_primaryAdapterName} -o {AdapterName} -m state --state RELATED,ESTABLISHED -j ACCEPT",
            cancellationToken).Vhc();
    }

    private void TryRemoveNat(IpNetwork ipNetwork)
    {
        var iptables = ipNetwork.IsV4 ? "iptables" : "ip6tables";

        // Remove NAT rule. try until no rule found
        var res = "ok";
        while (!string.IsNullOrEmpty(res)) {
            res = VhUtils.TryInvoke("Remove NAT rule", () =>
                ExecuteCommand(
                    $"{iptables} -t nat -D POSTROUTING -s {ipNetwork} -o {_primaryAdapterName} -j MASQUERADE"));
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

    private async Task SetDnsServersByResolvectl(IEnumerable<IPAddress> dnsServers, CancellationToken cancellationToken)
    {
        var allDns = string.Join(" ", dnsServers.Select(x => x.ToString()));
        var command = $"resolvectl dns {AdapterName} {allDns}";
        await ExecuteCommandAsync(command, cancellationToken).Vhc();
        await ExecuteCommandAsync($"resolvectl domain {AdapterName} \"~.\"", cancellationToken).Vhc();
    }

    private async Task SetDnsServersByResolvconf(IEnumerable<IPAddress> dnsServers, CancellationToken cancellationToken)
    {
        var dnsPayload = string.Join("\n", dnsServers.Select(x => $"nameserver {x}")) + "\n";
        var command = $"echo \"{dnsPayload}\" | resolvconf -a {AdapterName}";
        await ExecuteCommandAsync(command, cancellationToken).Vhc();
    }

    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
    protected override async Task SetDnsServers(IReadOnlyList<IPAddress> dnsServers,
        CancellationToken cancellationToken)
    {
        if (!dnsServers.Any())
            return;

        try {
            // --- Try systemd-resolved ---
            await SetDnsServersByResolvectl(dnsServers, cancellationToken);
        }
        catch (Exception ex) {
            // --- Fallback: use resolvconf ---
            VhLogger.Instance.LogWarning(ex, "Failed to set DNS using resolvectl. Trying fallback to resolvconf...");
            try {
                await SetDnsServersByResolvconf(dnsServers, cancellationToken);
            }
            catch (Exception fallbackEx) {
                throw new Exception(
                    $"Failed to set DNS using both resolvectl and resolvconf.\n" +
                    $"resolvectl error: {ex.Message}\n" +
                    $"resolvconf error: {fallbackEx.Message}", fallbackEx);
            }
        }
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
        var fd = _tunAdapterFd;
        if (fd == InvalidFd)
            return false;

        var packetBytes = ipPacket.GetUnderlyingBufferUnsafe(_writeBuffer, out var bufferLength);

        // A TUN device writes a whole datagram or nothing, so there is no partial write to continue.
        var bytesWritten = LinuxAPI.write(fd, packetBytes, (nuint)bufferLength);
        if (bytesWritten == bufferLength)
            return true;

        // A short (but positive) write means a truncated packet was injected. This should not happen for a
        // TUN char device; drop it rather than tearing down the adapter.
        if (bytesWritten >= 0) {
            VhLogger.Instance.LogWarning(
                "Partial write to TUN device; dropping packet. Written: {Written}, Length: {Length}",
                bytesWritten, bufferLength);
            return true;
        }

        var errorCode = Marshal.GetLastWin32Error();
        return errorCode switch {
            // Buffer full (EAGAIN) or interrupted before any byte was written (EINTR): report not-sent and
            // let the parent send loop (SendPacketInternal) back off (WaitForTunWrite) and retry.
            OsConstants.Eagain or OsConstants.Eintr => false,
            _ => throw new PInvokeException("Could not write to TUN.", errorCode)
        };
    }

    protected override bool ReadPacket(byte[] buffer)
    {
        var fd = _tunAdapterFd;
        if (fd == InvalidFd)
            throw new IOException("TUN adapter is closed.");

        var bytesRead = LinuxAPI.read(fd, buffer, (nuint)buffer.Length);
        if (bytesRead > 0)
            return true;

        // check for errors
        var errorCode = Marshal.GetLastWin32Error();
        return errorCode switch {
            // No data available; the parent read loop polls (WaitForTunRead) and retries.
            OsConstants.Eagain => false,
            // Interrupted before any byte was read. Report it as "no data" and let the parent drive the
            // retry (it re-checks liveness and polls) rather than spinning here — EINTR is normal, not an
            // error, so it must not throw or count toward the I/O error threshold.
            OsConstants.Eintr => false,
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
        // AdapterRemove closes the fd AND removes the NAT/forwarding iptables rules. On a normal Dispose
        // that already happened via Stop(), but the finalizer path never runs Stop(), so we must clean
        // NAT here too. A leftover MASQUERADE rule referencing a deleted interface can break the host
        // network, so cleaning it (even on the finalizer thread) is the lesser evil versus leaking it.
        if (_tunAdapterFd != InvalidFd)
            AdapterRemove();

        base.DisposeUnmanaged();
    }

    ~LinuxTunVpnAdapter()
    {
        Dispose(false);
    }
}