﻿using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using SharpPcap;
using SharpPcap.WinDivert;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.VpnAdapters.Abstractions;
using VpnHood.Core.Toolkit.Net;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Core.Client.Device.WinDivert;

public class WinDivertVpnAdapter : IVpnAdapter
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    private readonly WinDivertDevice _device;
    private bool _disposed;
    private WinDivertHeader? _lastCaptureHeader;
    private PacketReceivedEventArgs? _packetReceivedEventArgs;
    private IPAddress? _virtualIpV4;
    private IPAddress? _virtualIpV6;
    private IPAddress? _clientInternalIpV4;
    private IPAddress? _clientInternalIpV6;

    public const short ProtectedTtl = 111;
    public event EventHandler<PacketReceivedEventArgs>? PacketReceivedFromInbound;
    public event EventHandler? Disposed;
    public event EventHandler? Stopped;
    public bool Started => _device.Started;
    public virtual bool CanSendPacketToOutbound => true;
    public virtual bool IsDnsServersSupported => false;
    public virtual bool IsNatSupported => false;

    public WinDivertVpnAdapter()
    {
        // initialize devices
        _device = new WinDivertDevice { Flags = 0 };
        _device.OnPacketArrival += Device_OnPacketArrival;

        // manage WinDivert file
        SetWinDivertDllFolder();
    }

    public virtual bool CanProtectSocket => true;

    public virtual void ProtectSocket(Socket socket)
    {
        socket.Ttl = ProtectedTtl;
    }

    public void SendPacketToInbound(IList<IPPacket> ipPackets)
    {
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++) {
            var ipPacket = ipPackets[i];
            SendPacket(ipPacket, false);
        }
    }

    public void SendPacketToInbound(IPPacket ipPacket)
    {
        SendPacket(ipPacket, false);
    }

    public void SendPacketToOutbound(IPPacket ipPacket)
    {
        SendPacket(ipPacket, true);
    }

    public void SendPacketToOutbound(IList<IPPacket> ipPackets)
    {
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++) {
            var ipPacket = ipPackets[i];
            SendPacket(ipPacket, true);
        }
    }

    private static string Ip(IpRange ipRange)
    {
        return ipRange.AddressFamily == AddressFamily.InterNetworkV6 ? "ipv6" : "ip";
    }

    public Task Start(VpnAdapterOptions options, CancellationToken cancellationToken)
    {
        if (_disposed)
            throw new ObjectDisposedException(VhLogger.FormatType(this));

        if (Started)
            throw new InvalidOperationException("VpnAdapter has been already started.");

        if (options.UseNat)
            throw new NotSupportedException("WinDivert does not support NAT.");

        _virtualIpV4 = options.VirtualIpNetworkV4?.Prefix;
        _virtualIpV6 = options.VirtualIpNetworkV6?.Prefix;

        // create include and exclude phrases
        var phraseX = "true";
        var ipRanges = options.IncludeNetworks.ToIpRanges();
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

    public void Stop()
    {
        if (!Started)
            return;

        _device.StopCapture();
        Stopped?.Invoke(this, EventArgs.Empty);
    }

    private void SetInternalIp(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
            _clientInternalIpV4 = address;
        else
            _clientInternalIpV6 = address;
    }

    private IPAddress? GetInternalIp(IPVersion ipVersion)
    {
        return ipVersion == IPVersion.IPv4 ? _clientInternalIpV4 : _clientInternalIpV6;
    }

    private IPAddress? GetVirtualIp(IPVersion ipVersion)
    {
        return ipVersion == IPVersion.IPv4 ? _virtualIpV4 : _virtualIpV6;
    }


    private void Device_OnPacketArrival(object sender, PacketCapture e)
    {
        var rawPacket = e.GetPacket();
        var packet = Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);
        var ipPacket = packet.Extract<IPPacket>();

        _lastCaptureHeader = (WinDivertHeader)e.Header;

        // start trying to simulate tun
        SetInternalIp(ipPacket.SourceAddress);
        var virtualIp = GetVirtualIp(ipPacket.Version);
        if (virtualIp == null) {
            VhLogger.Instance.LogDebug("The device arrival packet is not supported: {Packet}",
                VhLogger.FormatIpPacket(ipPacket.ToString()!));
            return;
        }

        ipPacket.SourceAddress = virtualIp;
        UpdateIpPacket(ipPacket);
        // end trying to simulate tun

        ProcessPacketReceivedFromInbound(ipPacket);
    }

    protected virtual void ProcessPacketReceivedFromInbound(IPPacket ipPacket)
    {
        // create the event args. for performance, we will reuse the same instance
        _packetReceivedEventArgs ??= new PacketReceivedEventArgs(new IPPacket[1]);

        try {
            _packetReceivedEventArgs.IpPackets[0] = ipPacket;
            PacketReceivedFromInbound?.Invoke(this, _packetReceivedEventArgs);
        }
        catch (Exception ex) {
            VhLogger.Instance.Log(LogLevel.Error, ex,
                "Error in processing packet Packet: {Packet}", VhLogger.FormatIpPacket(ipPacket.ToString()!));
        }
    }

    private void SendPacket(IPPacket ipPacket, bool outbound)
    {
        if (_lastCaptureHeader == null)
            throw new InvalidOperationException("Could not send any data without receiving a packet.");

        // start trying to simulate tun
        var internalIp = GetInternalIp(ipPacket.Version);
        if (internalIp == null)
            throw new InvalidOperationException("Could not send packet to inbound. there is no internal IP.");

        if (outbound)
            ipPacket.SourceAddress = internalIp;
        else
            ipPacket.DestinationAddress = internalIp;

        UpdateIpPacket(ipPacket);
        // end trying to simulate tun

        // send by a device
        _lastCaptureHeader.Flags = outbound ? WinDivertPacketFlags.Outbound : 0;
        _device.SendPacket(ipPacket.Bytes, _lastCaptureHeader);
    }

    private static void UpdateIpPacket(IPPacket ipPacket)
    {
        if (ipPacket.Protocol is ProtocolType.Icmp)
            ipPacket.Extract<IcmpV4Packet>()?.UpdateIcmpChecksum();

        if (ipPacket.Protocol is ProtocolType.IcmpV6)
            ipPacket.Extract<IcmpV6Packet>()?.UpdateIcmpChecksum();

        if (ipPacket is IPv4Packet ipV4Packet)
            ipV4Packet.UpdateIPChecksum();

        ipPacket.UpdateCalculatedValues();
    }

    // Note: System may load WinDivert driver into memory and lock it, so we'd better to copy it into a temporary folder 
    private static void SetWinDivertDllFolder()
    {
        // I got sick trying to add it to nuget as a native library in (x86/x64) folder, OOF!
        var destinationFolder = Path.Combine(Path.GetTempPath(), "VpnHood-WinDivertDevice", "2.2.2");
        var requiredFiles = new[] { "WinDivert.dll", "WinDivert64.sys" };

        // extract WinDivert
        var checkFiles = requiredFiles.Select(x => Path.Combine(destinationFolder, x));
        if (checkFiles.Any(x => !File.Exists(x))) {
            using var memStream = new MemoryStream(Resource.WinDivertLibZip);
            using var zipArchive = new ZipArchive(memStream);
            zipArchive.ExtractToDirectory(destinationFolder, true);
        }

        LoadLibrary(Path.Combine(destinationFolder, "WinDivert.dll"));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // stop the device
        Stop();

        _device.Dispose();
        Disposed?.Invoke(this, EventArgs.Empty);
    }
}