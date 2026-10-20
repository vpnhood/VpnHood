using VpnHood.Net.Toolkit.Net;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server.Access;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Server.Listeners;

// the server's UDP listeners: each hands its datagrams to their sessions
internal class UdpListenerHost(SessionManager sessionManager) : IUdpDatagramHandler, IDisposable
{
    private readonly EventReporter _invalidSessionReporter = new("Invalid UDP session.", GeneralEventId.UdpSign);
    private readonly List<UdpChannelTransmitter> _transmitters = [];
    private bool _disposed;

    // only report transmitters that are still running; a transmitter that disposed itself
    // (e.g. its socket died) must not be advertised in the Hello response
    public IReadOnlyList<IPEndPoint> EndPoints =>
        _transmitters.Where(x => x.Connected).Select(x => x.LocalEndPoint).ToArray();

    public Task<IReadOnlyList<ServerHostEndPointStatus>> Configure(
        IReadOnlyList<IPEndPoint> ipEndPoints, TransferBufferSize? bufferSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // stop transmitters that are not in the list; also drop dead ones so they get recreated below
        foreach (var transmitter in _transmitters
                     .Where(x => !x.Connected || !ipEndPoints.Contains(x.LocalEndPoint)).ToArray()) {
            VhLogger.Instance.LogInformation("Stop listening on UdpEndPoint: {UdpEndPoint}",
                VhLogger.Format(transmitter.LocalEndPoint));
            transmitter.Dispose();
            _transmitters.Remove(transmitter);
        }

        // start new transmitters
        var endPointStatuses = new List<ServerHostEndPointStatus>();
        foreach (var ipEndPoint in ipEndPoints) {
            try {
                if (ipEndPoint.Port == 0)
                    throw new InvalidOperationException("UDP port has not been specified.");

                if (_transmitters.Any(x => x.LocalEndPoint.Equals(ipEndPoint))) {
                    endPointStatuses.Add(new ServerHostEndPointStatus { Protocol = ChannelProtocol.Udp, EndPoint = ipEndPoint });
                    continue;
                }

                VhLogger.Instance.LogInformation("Start listening on UdpEndPoint: {UdpEndPoint}",
                    VhLogger.Format(ipEndPoint));

                var transmitter = CreateListener(ipEndPoint);
                _transmitters.Add(transmitter);
                endPointStatuses.Add(new ServerHostEndPointStatus { Protocol = ChannelProtocol.Udp, EndPoint = ipEndPoint });
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Error listening on UdpEndPoint: {UdpEndPoint}",
                    VhLogger.Format(ipEndPoint));
                endPointStatuses.Add(new ServerHostEndPointStatus {
                    Protocol = ChannelProtocol.Udp,
                    EndPoint = ipEndPoint,
                    Error = ex.ToApiError()
                });
            }
        }

        // reconfigure all transmitters
        foreach (var transmitter in _transmitters)
            transmitter.BufferSize = bufferSize ?? ServerTransportDefaults.UdpChannelBufferSize;

        IReadOnlyList<ServerHostEndPointStatus> ret = endPointStatuses;
        return Task.FromResult(ret);
    }

    // the datagram's session takes it; a datagram with no session, a closed one or one below the UDP protocol is only
    // counted
    public void OnDatagramReceived(UdpChannelTransmitter listener, Memory<byte> datagram, IPEndPoint remoteEndPoint)
    {
        var session = sessionManager.GetSessionById(UdpDatagram.ReadSessionId(datagram.Span));
        if (session?.ReceiveUdp(listener, datagram, remoteEndPoint) != true)
            _invalidSessionReporter.Raise();
    }

    private UdpChannelTransmitter CreateListener(IPEndPoint ipEndPoint)
    {
        var udpClient = new UdpClient(ipEndPoint);
        try {
            return new UdpChannelTransmitter(udpClient, this);
        }
        catch {
            udpClient.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        foreach (var transmitter in _transmitters)
            transmitter.Dispose();
        _transmitters.Clear();
        _invalidSessionReporter.Dispose();
    }
}
