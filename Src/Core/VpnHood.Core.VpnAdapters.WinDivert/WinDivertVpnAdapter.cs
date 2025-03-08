﻿using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using SharpPcap;
using SharpPcap.WinDivert;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Collections;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.VpnAdapters.Abstractions;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Core.VpnAdapters.WinDivert;

public class WinDivertVpnAdapter(WinDivertVpnAdapterSettings adapterSettings) :
    TunVpnAdapter(adapterSettings)
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    private WinDivertDevice? _device;
    private WinDivertHeader? _lastCaptureHeader;
    private readonly IPPacket[] _ipPackets = new IPPacket[1];
    private readonly List<IpNetwork> _includeIpNetworks = new();
    private IPAddress[] _dnsServers = [];
    private readonly TimeoutDictionary<ushort, TimeoutItem<IPAddress>> _lastDnsServersV4 = new(TimeSpan.FromSeconds(30));
    private readonly TimeoutDictionary<ushort, TimeoutItem<IPAddress>> _lastDnsServersV6 = new(TimeSpan.FromSeconds(30));

    public const short ProtectedTtl = 111;
    public override bool IsDnsServerSupported => true;
    public override bool IsAppFilterSupported => false;
    public override bool IsNatSupported => false;
    protected override bool CanProtectSocket => true;
    protected override string? AppPackageId => null;

    protected override Task AdapterAdd(CancellationToken cancellationToken)
    {
        // initialize devices
        _device = new WinDivertDevice { Flags = 0 };
        _device.OnPacketArrival += Device_OnPacketArrival;

        // clean old configs
        _includeIpNetworks.Clear();

        // manage WinDivert file
        SetWinDivertDllFolder();
        return Task.CompletedTask;
    }

    protected override void AdapterRemove()
    {
        AdapterClose();
        _device?.Dispose();
        _device = null;
    }

    protected override Task AdapterOpen(CancellationToken cancellationToken)
    {
        if (_device == null)
            throw new InvalidOperationException("Device is not initialized.");

        // create include and exclude phrases
        var phraseX = "true";
        var ipRanges = _includeIpNetworks.ToIpRanges();
        if (!ipRanges.IsAll()) {
            var phrases = ipRanges.Select(x => x.FirstIpAddress.Equals(x.LastIpAddress)
                ? $"{Ip(x)}.DstAddr=={x.FirstIpAddress}"
                : $"({Ip(x)}.DstAddr>={x.FirstIpAddress} and {Ip(x)}.DstAddr<={x.LastIpAddress})");
            var phrase = string.Join(" or ", phrases);
            phraseX += $" and ({phrase})";
        }

        // add outbound; filter loopback
        var filter = $"(ip.TTL!={ProtectedTtl} or ipv6.HopLimit!={ProtectedTtl}) and " +
                     $"outbound and !loopback and " +
                     $"(udp.DstPort==53 or ({phraseX}))";

        filter = filter.Replace("ipv6.DstAddr>=::", "ipv6"); // WinDivert bug
        try {
            _device.Filter = filter;
            _device.Open(new DeviceConfiguration());
            _device.StartCapture();
        }
        catch (Exception ex) {
            if (ex.Message.IndexOf("access is denied", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new Exception(
                    "Access denied! Could not open WinDivert driver! Make sure the app is running with admin privilege.",
                    ex);
            throw;
        }

        return Task.CompletedTask;
    }

    protected override void StartReadingPackets()
    {
        // let device event dispatcher do the job
    }

    protected override void AdapterClose()
    {
        _device?.StopCapture();
        _device?.Close();
    }


    protected override Task SetDnsServers(IPAddress[] dnsServers, CancellationToken cancellationToken)
    {
        _dnsServers = dnsServers;
        return Task.CompletedTask;
    }

    protected override Task AddRoute(IpNetwork ipNetwork, IPAddress gatewayIp, CancellationToken cancellationToken)
    {
        _includeIpNetworks.Add(ipNetwork);
        return Task.CompletedTask;
    }

    protected override Task SetAllowedApps(string[] packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("App filtering is not supported on LinuxTun.");

    protected override Task SetDisallowedApps(string[] packageIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException("App filtering is not supported on LinuxTun.");

    protected override Task AddAddress(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        return Task.CompletedTask; // nothing to do
    }

    protected override Task SetMetric(int metric, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        return Task.CompletedTask; // nothing to do
    }

    protected override Task SetSessionName(string sessionName, CancellationToken cancellationToken)
    {
        return Task.CompletedTask; // nothing to do
    }

    protected override Task AddNat(IpNetwork ipNetwork, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("NAT is not supported on LinuxTun.");
    }

    protected override Task SetMtu(int mtu, bool ipV4, bool ipV6, CancellationToken cancellationToken)
    {
        // todo must be implemented
        return Task.CompletedTask;
    }

    protected override IPPacket ReadPacket(int mtu)
    {
        throw new NotSupportedException("ReadPacket is not supported on WinDivert.");
    }

    protected override void WaitForTunRead()
    {
        throw new NotSupportedException("ReadPacket is not supported on WinDivert and override by StartReadingPacket.");
    }

    protected override void ProtectSocket(Socket socket)
    {
        socket.Ttl = ProtectedTtl;
    }

    private static string Ip(IpRange ipRange)
    {
        return ipRange.AddressFamily == AddressFamily.InterNetworkV6 ? "ipv6" : "ip";
    }

    private void Device_OnPacketArrival(object sender, PacketCapture e)
    {
        var rawPacket = e.GetPacket();
        var packet = Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);
        var ipPacket = packet.Extract<IPPacket>();

        _lastCaptureHeader = (WinDivertHeader)e.Header;
        ProcessPacketReceived(ipPacket);
    }

    protected virtual void ProcessPacketReceived(IPPacket ipPacket)
    {
        // simulate adapter network
        SimulateAdapterNetwork(ipPacket, true);

        // simulate dns servers
        SimulateDnsServers(ipPacket, true);

        // create the event args. for performance, we will reuse the same instance
        _ipPackets[0] = ipPacket;
        InvokeReadPackets(_ipPackets);
    }

    //todo rename to write packet
    protected void SendPacket(IPPacket ipPacket, bool outbound)
    {
        if (_lastCaptureHeader == null)
            throw new InvalidOperationException("Could not send any data without receiving a packet.");

        if (_device == null)
            throw new InvalidOperationException("Device is not initialized.");

        if (!outbound) {
            // simulate adapter network
            SimulateAdapterNetwork(ipPacket, false);

            // simulate dns servers
            SimulateDnsServers(ipPacket, false);
        }

        // send by a device
        _lastCaptureHeader.Flags = outbound ? WinDivertPacketFlags.Outbound : 0;
        _device.SendPacket(ipPacket.Bytes, _lastCaptureHeader);
    }

    protected override bool WritePacket(IPPacket ipPacket)
    {
#if DEBUG
        if (GetIpNetwork(ipPacket.Version)?.Contains(ipPacket.DestinationAddress) is null or false)
            throw new NotSupportedException("This adapter can send packets outside of its network.");
#endif
        SendPacket(ipPacket, false);
        return true;
    }

    protected override void WaitForTunWrite()
    {
        Thread.Sleep(1);
    }

    private void SimulateAdapterNetwork(IPPacket ipPacket, bool read)
    {
        if (read) {
            var adapterIp = GetIpNetwork(ipPacket.Version)?.Prefix;
            if (adapterIp == null) {
                VhLogger.Instance.LogDebug("The arrival packet is not supported : {Packet}",
                    VhLogger.FormatIpPacket(ipPacket.ToString()!));
                return;
            }

            ipPacket.SourceAddress = adapterIp;
        }
        else {
            var primaryAdapterIp = GetPrimaryAdapterIp(ipPacket.Version);
            if (primaryAdapterIp == null)
                throw new InvalidOperationException("Could not send packet to inbound. there is no internal IP.");

            ipPacket.DestinationAddress = primaryAdapterIp;
        }

        ipPacket.UpdateIpChecksum();
    }

    private void SimulateDnsServers(IPPacket ipPacket, bool read)
    {
        if (ipPacket.Protocol != ProtocolType.Udp || _dnsServers.Length == 0)
            return;
        
        var udpPacket = ipPacket.ExtractUdp();
        var lastDnsServers = ipPacket.Version == IPVersion.IPv4 ? _lastDnsServersV4 : _lastDnsServersV6;

        if (read) {
            // check if the packet is a dns query
            if (udpPacket.DestinationPort !=53) 
                return;

            // update the last dns server
            lastDnsServers.AddOrUpdate(udpPacket.SourcePort, new TimeoutItem<IPAddress>(ipPacket.DestinationAddress));

            // select a random dns server by matching its ip version
            var filteredDnsServers = _dnsServers
                .Where(dns => dns.AddressFamily == ipPacket.SourceAddress.AddressFamily)
                .ToArray();

            // select a random dns server
            var dnsServer = filteredDnsServers.Length > 0
                ? filteredDnsServers[new Random().Next(filteredDnsServers.Length)]
                : null;

            // update the dns server
            if (dnsServer != null) {
                ipPacket.DestinationAddress = dnsServer;
                ipPacket.UpdateIpChecksum();
            }
        }
        else {
            // check if the packet is a dns response
            if (udpPacket.SourcePort !=53) 
                return;

            // update the last dns server
            if (lastDnsServers.TryGetValue(udpPacket.DestinationPort, out var dnsServer))
                ipPacket.SourceAddress = dnsServer.Value;

            ipPacket.UpdateIpChecksum();
        }
    }


    // WARNING: System may load WinDivert driver into memory and lock it, so we'd better to copy it into a temporary folder 
    // We don't rely on WinDiver anymore so we ignore this problem
    private static void SetWinDivertDllFolder()
    {
        // I got sick trying to add it to nuget as a native library in (x86/x64) folder, OOF!
        var destinationFolder = Path.Combine(Path.GetTempPath(), "VpnHood", "WinDivert", "2.2.2");
        var requiredFiles = new[] { "WinDivert.dll", "WinDivert64.sys" };

        // extract WinDivert
        var checkFiles = requiredFiles.Select(x => Path.Combine(destinationFolder, x));
        if (checkFiles.Any(x => !File.Exists(x))) {
            using var memStream = new MemoryStream(Resources.WinDivertLibZip);
            using var zipArchive = new ZipArchive(memStream);
            zipArchive.ExtractToDirectory(destinationFolder, true);
        }

        LoadLibrary(Path.Combine(destinationFolder, "WinDivert.dll"));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // The adapter is an unmanaged resource; it must be closed if it is open
        if (_device != null)
            AdapterRemove();
    }
}