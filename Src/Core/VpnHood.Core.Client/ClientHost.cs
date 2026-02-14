using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Net.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.Exceptions;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client;

internal class ClientHost(
    ClientStreamHandler streamHandler,
    IPAddress catcherAddressIpV4,
    IPAddress catcherAddressIpV6)
    : IDisposable
{
    private bool _disposed;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Nat _nat = new(true);
    private TcpListener? _tcpListenerIpV4;
    private TcpListener? _tcpListenerIpV6;
    private IPEndPoint? _localEndpointIpV4;
    private IPEndPoint? _localEndpointIpV6;

    public IPAddress CatcherAddressIpV4 => catcherAddressIpV4;
    public IPAddress CatcherAddressIpV6 => catcherAddressIpV6;
    public event EventHandler<IpPacket>? PacketReceived;

    public void DropCurrentConnections()
    {
        _nat.RemoveAll();
    }

    public bool IsOwnPacket(IpPacket ipPacket)
    {
        if (ipPacket.Protocol != IpProtocol.Tcp)
            return false;

        return CatcherAddressIpV4.SpanEquals(ipPacket.DestinationAddressSpan) ||
               CatcherAddressIpV6.SpanEquals(ipPacket.DestinationAddressSpan);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var logScope = VhLogger.Instance.BeginScope("ClientHost");
        VhLogger.Instance.LogInformation("Starting ClientHost...");

        // IpV4
        _tcpListenerIpV4 = new TcpListener(IPAddress.Any, 0);
        _tcpListenerIpV4.Start();
        _localEndpointIpV4 = (IPEndPoint)_tcpListenerIpV4.LocalEndpoint; //it is slow; make sure to cache it
        VhLogger.Instance.LogInformation("ClientHost is listening. EndPoint: {EndPoint}",
            VhLogger.Format(_localEndpointIpV4));
        _ = AcceptTcpClientLoop(_tcpListenerIpV4);

        // IpV6
        try {
            _tcpListenerIpV6 = new TcpListener(IPAddress.IPv6Any, 0);
            _tcpListenerIpV6.Start();
            _localEndpointIpV6 = (IPEndPoint)_tcpListenerIpV6.LocalEndpoint; //it is slow; make sure to cache it
            VhLogger.Instance.LogInformation("ClientHost is listening. EndPoint: {EndPoint}",
                VhLogger.Format(_localEndpointIpV6));
            _ = AcceptTcpClientLoop(_tcpListenerIpV6);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex,
                "Could not create a listener. EndPoint: {EndPoint}",
                VhLogger.Format(new IPEndPoint(IPAddress.IPv6Any, 0)));
        }
    }

    private async Task AcceptTcpClientLoop(TcpListener tcpListener)
    {
        var localEp = (IPEndPoint)tcpListener.LocalEndpoint;

        try {
            while (!_cancellationTokenSource.IsCancellationRequested) {
                // config tcpOrgClient
                var tcpClient = await tcpListener.AcceptTcpClientAsync().Vhc();
                VhUtils.ConfigTcpClient(tcpClient, null, null);
                var tcpConnection = new TcpConnection(tcpClient, isServer: false, connectionName: "app");
                _ = ProcessConnection(tcpConnection, _cancellationTokenSource.Token);
            }
        }
        catch (Exception ex) {
            if (!_disposed)
                VhLogger.LogError(GeneralEventId.Request, ex, "");
        }
        finally {
            VhLogger.Instance.LogInformation("ClientHost Listener has been closed. LocalEp: {localEp}", localEp);
        }
    }

    // this method should not be called in multi-thread, the return buffer is shared and will be modified on next call
    public void ProcessOutgoingPacket(IpPacket ipPacket, bool? isInIpRange)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PacketLogger.LogPacket(ipPacket, "Processing a ClientHost packet...");

        // check packet type
        if (_localEndpointIpV4 == null)
            throw new InvalidOperationException(
                $"{nameof(_localEndpointIpV4)} has not been initialized! Did you call Start!");

        var catcherAddress = ipPacket.Version == IpVersion.IPv4 ? CatcherAddressIpV4 : CatcherAddressIpV6;
        var localEndPoint = ipPacket.Version == IpVersion.IPv4 ? _localEndpointIpV4 : _localEndpointIpV6;

        try {
            var tcpPacket = ipPacket.ExtractTcp();

            // check local endpoint
            if (localEndPoint == null)
                throw new Exception("There is no localEndPoint registered for this packet.");

            // redirect to inbound
            if (catcherAddress.SpanEquals(ipPacket.DestinationAddressSpan)) {
                var natItem = (NatItemEx?)_nat.Resolve(ipPacket.Version, ipPacket.Protocol, tcpPacket.DestinationPort)
                              ?? throw new NatEndpointNotFoundException(
                                  "Could not find incoming tcp destination in NAT.");

                ipPacket.SourceAddress = natItem.DestinationAddress;
                ipPacket.DestinationAddress = natItem.SourceAddress;
                tcpPacket.SourcePort = natItem.DestinationPort;
                tcpPacket.DestinationPort = natItem.SourcePort;
            }

            // Redirect outbound to the local address
            else {
                var sync = tcpPacket is { Synchronize: true, Acknowledgment: false };
                var syncCustomData = sync && isInIpRange != null
                    ? new SyncCustomData { IsInIpRange = isInIpRange.Value }
                    : (SyncCustomData?)null;

                // add to nat if it is sync packet
                var natItem = syncCustomData != null
                    ? _nat.Add(ipPacket, true)
                    : _nat.Get(ipPacket) ??
                      throw new NatEndpointNotFoundException("Could not find outgoing tcp destination in NAT.");

                // set customData
                if (syncCustomData != null)
                    natItem.CustomData = syncCustomData;

                // rewrite packet by changing source/destination address and port
                tcpPacket.SourcePort = natItem.NatId; // 1
                ipPacket.DestinationAddress = ipPacket.SourceAddress; // 2
                ipPacket.SourceAddress = catcherAddress; //3
                tcpPacket.DestinationPort = (ushort)localEndPoint.Port; //4
            }

            ipPacket.UpdateAllChecksums();
            PacketReceived?.Invoke(this, ipPacket);
        }
        catch (NatEndpointNotFoundException ex) when (ipPacket.Protocol == IpProtocol.Tcp) {
            var resultPacket = PacketBuilder.BuildTcpResetReply(ipPacket);
            PacketReceived?.Invoke(this, resultPacket);
            throw new PacketDropException("Packet dropped and TCP reset sent.", ex);
        }
    }

    private async Task ProcessConnection(IConnection connection, CancellationToken cancellationToken)
    {
        try {
            // get original remote from NAT
            var remoteEndPoint = connection.RemoteEndPoint;
            var ipVersion = remoteEndPoint.IpVersion();
            var natItem =
                (NatItemEx?)_nat.Resolve(ipVersion, IpProtocol.Tcp, (ushort)remoteEndPoint.Port) ??
                throw new Exception(
                    $"Could not resolve original remote from NAT! RemotePort: {remoteEndPoint.Port}");

            // check invalid income
            var catcherAddress = ipVersion == IpVersion.IPv4 ? CatcherAddressIpV4 : CatcherAddressIpV6;
            if (!Equals(connection.RemoteEndPoint.Address, catcherAddress))
                throw new Exception("TcpProxy rejected an outbound connection!");

            var syncCustomData = natItem.CustomData as SyncCustomData?;
            var isInIpRange = syncCustomData?.IsInIpRange ?? true; // default to true if no custom data
            var hostEndPoint = new IPEndPoint(natItem.DestinationAddress, natItem.DestinationPort);
            await streamHandler.ProcessConnection(connection, hostEndPoint, isInIpRange, cancellationToken).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(GeneralEventId.Stream, ex, "Could not process a tcp stream request.");
            await connection.DisposeAsync();
        }
    }
  

    public void Dispose()
    {
        if (_disposed)
            return;

        _cancellationTokenSource.TryCancel();
        _cancellationTokenSource.Dispose();

        _tcpListenerIpV4?.Stop();
        _tcpListenerIpV6?.Stop();
        _nat.Dispose();
        PacketReceived = null;

        _disposed = true;
    }

    

    public struct SyncCustomData
    {
        public required bool IsInIpRange { get; init; }
    }
}