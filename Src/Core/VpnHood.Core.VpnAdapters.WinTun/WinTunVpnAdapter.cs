using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Core.Toolkit.Exceptions;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.VpnAdapters.Abstractions;
using VpnHood.Core.VpnAdapters.WinTun.WinNative;

namespace VpnHood.Core.VpnAdapters.WinTun;

public class WinTunVpnAdapter(WinTunVpnAdapterOptions adapterOptions)
    : TunVpnAdapter(adapterOptions)
{
    private readonly int _ringCapacity = adapterOptions.RingCapacity;
    private IntPtr _tunAdapter;
    private IntPtr _tunSession;
    private IntPtr _readEvent;

    public const int MinRingCapacity = 0x20000; // 128kiB
    public const int MaxRingCapacity = 0x4000000; // 64MiB
    public override bool IsNatSupported => true;

    protected override Task OpenAdapter(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Initializing WinTun Adapter...");
        _tunAdapter = WinTunApi.WintunCreateAdapter(AdapterName, "WinTun", IntPtr.Zero);
        if (_tunAdapter == IntPtr.Zero)
            throw new Win32Exception("Failed to create WinTun adapter.");

        // start WinTun session
        Logger.LogInformation("Starting WinTun session...");
        _tunSession = WinTunApi.WintunStartSession(_tunAdapter, _ringCapacity);
        if (_tunSession == IntPtr.Zero)
            throw new Win32Exception("Failed to start WinTun session.");

        // create an event object to wait for packets
        Logger.LogDebug("Creating event object for WinTun...");
        _readEvent = WinTunApi.WintunGetReadWaitEvent(_tunSession); // do not close this handle by documentation

        return Task.CompletedTask;
    }

    protected override async Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        if (ipV4)
            await OsUtils.ExecuteCommandAsync("netsh", $"interface ipv4 set interface \"{AdapterName}\" metric={metric}", cancellationToken);

        if (ipV6)
            await OsUtils.ExecuteCommandAsync("netsh", $"interface ipv6 set interface \"{AdapterName}\" metric={metric}", cancellationToken);
    }

    protected override async Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        var command = ipNetwork.IsV4
            ? $"interface ipv4 set address \"{AdapterName}\" static {ipNetwork}"
            : $"interface ipv6 set address \"{AdapterName}\" {ipNetwork}";

        await OsUtils.ExecuteCommandAsync("netsh", command, cancellationToken);
    }

    protected override async Task AddRoute(IpNetwork ipNetwork, IPAddress gatewayIp, CancellationToken cancellationToken)
    {
        var command = ipNetwork.IsV4
            ? $"interface ipv4 add route {ipNetwork} \"{AdapterName}\" {gatewayIp}"
            : $"interface ipv6 add route {ipNetwork} \"{AdapterName}\" {gatewayIp}";

        await OsUtils.ExecuteCommandAsync("netsh", command, cancellationToken);
    }

    protected override async Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        if (ipV4)
            await OsUtils.ExecuteCommandAsync("netsh", $"interface ipv4 set subinterface \"{AdapterName}\" mtu={mtu}", cancellationToken);

        if (ipV4)
            await OsUtils.ExecuteCommandAsync("netsh", $"interface ipv6 set subinterface \"{AdapterName}\" mtu={mtu}", cancellationToken);
    }

    protected override async Task SetDnsServers(IPAddress[] ipAddresses, CancellationToken cancellationToken)
    {
        foreach (var ipAddress in ipAddresses) {
            var command = $"interface ip add dns \"{AdapterName}\" {ipAddress}";
            await OsUtils.ExecuteCommandAsync("netsh", command, cancellationToken);
        }
    }

    protected override async Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        // Remove previous NAT if any
        TryRemoveNat(ipNetwork);

        // Configure NAT with iptables
        if (ipNetwork.IsV4) {
            // let's throw error in ipv4
            await ExecutePowerShellCommandAsync($"New-NetNat -Name {AdapterName}Nat -InternalIPInterfaceAddressPrefix {ipNetwork}",
                cancellationToken).VhConfigureAwait();
        }
        else {
            // ignore exception in ipv6 on windows
            await VhUtils.TryInvokeAsync("Configuring NAT for IPv6", () =>
                ExecutePowerShellCommandAsync($"New-NetNat -Name {AdapterName}NatIpV6 -InternalIPInterfaceAddressPrefix {ipNetwork}",
                    cancellationToken));
        }
    }

    private static void TryRemoveNat(IpNetwork ipNetwork)
    {
        // Remove NAT rule. try until no rule found
        VhUtils.TryInvoke("Remove NAT rule", () =>
            ExecutePowerShellCommand(
                $"Get-NetNat | Where-Object {{ $_.InternalIPInterfaceAddressPrefix -eq '{ipNetwork}' }} | Remove-NetNat -Confirm:$false"));
    }

    private static Task<string> ExecutePowerShellCommandAsync(string command, CancellationToken cancellationToken)
    {
        var ps = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"";
        return OsUtils.ExecuteCommandAsync("powershell.exe", ps, cancellationToken);
    }

    private static string ExecutePowerShellCommand(string command)
    {
        var ps = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"";
        return OsUtils.ExecuteCommand("powershell.exe", ps);
    }

    protected override void WaitForTunRead()
    {
        var result = Kernel32.WaitForSingleObject(_readEvent, Kernel32.Infinite);
        if (result == Kernel32.WaitObject0)
            return;

        throw result == Kernel32.WaitFailed 
            ? new Win32Exception() 
            : new PInvokeException("Unexpected result from WaitForSingleObject", (int)result);
    }


    protected override void ReadPackets(List<IPPacket> packetList, int mtu)
    {
        const int maxErrorCount = 10;
        var errorCount = 0;
        while (Started && packetList.Count < packetList.Capacity) {
            var tunReceivePacket = WinTunApi.WintunReceivePacket(_tunSession, out var size);
            var lastError = (WintunReceivePacketError)Marshal.GetLastWin32Error();
            if (tunReceivePacket != IntPtr.Zero) {
                errorCount = 0; // reset the error count
                try {
                    // read the packet
                    var buffer = new byte[size];
                    Marshal.Copy(tunReceivePacket, buffer, 0, size);
                    var ipPacket = Packet.ParsePacket(LinkLayers.Raw, buffer).Extract<IPPacket>();
                    packetList.Add(ipPacket);
                }
                finally {
                    WinTunApi.WintunReleaseReceivePacket(_tunSession, tunReceivePacket);
                }
                continue;
            }

            switch (lastError) {
                case WintunReceivePacketError.NoMoreItems:
                    return;

                case WintunReceivePacketError.HandleEof:
                    if (Started)
                        throw new InvalidOperationException("WinTun adapter has been closed.");
                    return;

                case WintunReceivePacketError.InvalidData:
                    Logger.LogWarning("Invalid data received from WinTun adapter.");
                    if (errorCount++ > maxErrorCount)
                        throw new InvalidOperationException("Too many invalid data received from WinTun adapter."); // read the next packet
                    continue; // read the next packet

                default:
                    Logger.LogDebug("Unknown error in reading packet from WinTun. LastError: {lastError}", lastError);
                    if (errorCount++ > maxErrorCount)
                        throw new InvalidOperationException("Too many errors in reading packet from WinTun."); // read the next packet
                    continue; // read the next packet
            }
        }
    }

    protected override void WaitForTunWrite()
    {
        Thread.Sleep(1);
    }

    protected override bool WritePacket(IPPacket ipPacket)
    {
        var packetBytes = ipPacket.Bytes;

        // Allocate memory for the packet inside WinTun ring buffer
        var packetMemory = WinTunApi.WintunAllocateSendPacket(_tunSession, packetBytes.Length); // thread-safe
        if (packetMemory == IntPtr.Zero) 
            return false;


        // Copy the raw packet data into WinTun memory
        Marshal.Copy(packetBytes, 0, packetMemory, packetBytes.Length);

        // Send the packet through WinTun
        WinTunApi.WintunSendPacket(_tunSession, packetMemory); // thread-safe
        return true;
    }

    protected override void CloseAdapter()
    {
        if (_tunSession != IntPtr.Zero) {
            WinTunApi.WintunEndSession(_tunSession);
            _tunSession = IntPtr.Zero;
        }

        // do not close this handle by documentation
        _readEvent = IntPtr.Zero;

        // close the adapter
        if (_tunAdapter != IntPtr.Zero) {
            WinTunApi.WintunCloseAdapter(_tunAdapter);
            _tunAdapter = IntPtr.Zero;
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // The adapter is an unmanaged resource; it must be closed if it is open
        if (_tunAdapter != IntPtr.Zero)
            CloseAdapter();
    }

}