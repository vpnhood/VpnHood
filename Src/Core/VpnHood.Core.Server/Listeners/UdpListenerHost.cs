using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server.Access;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Server.Listeners;

internal class UdpListenerHost(SessionManager sessionManager) : IDisposable
{
    private readonly List<UdpChannelTransmitter> _transmitters = [];
    private bool _disposed;

    public IReadOnlyList<IPEndPoint> EndPoints =>
        _transmitters.Select(x => x.LocalEndPoint).ToArray();

    public Task<IReadOnlyList<ServerHostEndPointStatus>> Configure(
        IReadOnlyList<IPEndPoint> ipEndPoints, TransferBufferSize? bufferSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // UDP port zero must be specified in preparation
        if (ipEndPoints.Any(x => x.Port == 0))
            throw new InvalidOperationException("UDP port has not been specified.");

        // stop transmitters that are not in the list
        foreach (var transmitter in _transmitters
                     .Where(x => !ipEndPoints.Contains(x.LocalEndPoint)).ToArray()) {
            VhLogger.Instance.LogInformation("Stop listening on UdpEndPoint: {UdpEndPoint}",
                VhLogger.Format(transmitter.LocalEndPoint));
            transmitter.Dispose();
            _transmitters.Remove(transmitter);
        }

        // start new transmitters
        var endPointStatuses = new List<ServerHostEndPointStatus>();
        foreach (var ipEndPoint in ipEndPoints) {
            try {
                if (_transmitters.Any(x => x.LocalEndPoint.Equals(ipEndPoint))) {
                    endPointStatuses.Add(new ServerHostEndPointStatus { Protocol = ChannelProtocol.Udp, EndPoint = ipEndPoint });
                    continue;
                }

                VhLogger.Instance.LogInformation("Start listening on UdpEndPoint: {UdpEndPoint}",
                    VhLogger.Format(ipEndPoint));

                var transmitter = ServerUdpChannelTransmitter.Create(ipEndPoint, sessionManager);
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
            transmitter.BufferSize = bufferSize ?? TunnelDefaults.ServerUdpChannelBufferSize;

        IReadOnlyList<ServerHostEndPointStatus> ret = endPointStatuses;
        return Task.FromResult(ret);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        foreach (var transmitter in _transmitters)
            transmitter.Dispose();
        _transmitters.Clear();
    }
}
