using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Server.Listeners;

internal class UdpListenerHost(SessionManager sessionManager) : IDisposable
{
    private readonly List<UdpChannelTransmitter> _transmitters = [];
    private int _disposed;

    public IReadOnlyList<IPEndPoint> EndPoints =>
        _transmitters.Select(x => x.LocalEndPoint).ToArray();

    public void Configure(IPEndPoint[] udpEndPoints, TransferBufferSize? bufferSize)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        // UDP port zero must be specified in preparation
        if (udpEndPoints.Any(x => x.Port == 0))
            throw new InvalidOperationException("UDP port has not been specified.");

        // stop transmitters that are not in the list
        foreach (var transmitter in _transmitters
                     .Where(x => !udpEndPoints.Contains(x.LocalEndPoint)).ToArray()) {
            VhLogger.Instance.LogInformation("Stop listening on UdpEndPoint: {UdpEndPoint}",
                VhLogger.Format(transmitter.LocalEndPoint));
            transmitter.Dispose();
            _transmitters.Remove(transmitter);
        }

        // start new transmitters
        foreach (var udpEndPoint in udpEndPoints) {
            try {
                if (_transmitters.Any(x => x.LocalEndPoint.Equals(udpEndPoint)))
                    continue;

                VhLogger.Instance.LogInformation("Start listening on UdpEndPoint: {UdpEndPoint}",
                    VhLogger.Format(udpEndPoint));

                var transmitter = ServerUdpChannelTransmitter.Create(udpEndPoint, sessionManager);
                _transmitters.Add(transmitter);
            }
            catch (Exception ex) {
                ex.Data.Add("UdpEndPoint", udpEndPoint);
                throw;
            }
        }

        // reconfigure all transmitters
        foreach (var transmitter in _transmitters)
            transmitter.BufferSize = bufferSize ?? TunnelDefaults.ServerUdpChannelBufferSize;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) 
            return;

        foreach (var transmitter in _transmitters)
            transmitter.Dispose();
        _transmitters.Clear();
    }
}
