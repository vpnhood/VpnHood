using System.Net;
using PacketDotNet;
using VpnHood.Common.Collections;
using System.Threading.Tasks;
using System.Collections.Generic;
using VpnHood.Tunneling.Factory;
using System.Linq;
using System;
using VpnHood.Common.Utils;

namespace VpnHood.Tunneling;

public abstract class UdpProxyClient
{
    private readonly SocketFactory _socketFactory;
    private readonly TimeoutDictionary<string, UdpProxyWorker> _connectionMap;
    private readonly List<UdpProxyWorker> _udpWorkers = new();
    private DateTime _lastCleanupTime = FastDateTime.Now;

    public abstract Task OnPacketReceived(IPPacket packet);
    public TimeSpan Timeout { get; }
    public int UdpClientCount { get { lock (_udpWorkers) return _udpWorkers.Count; } }

    protected UdpProxyClient(SocketFactory socketFactory, TimeSpan? timeout)
    {
        _socketFactory = socketFactory;
        Timeout = timeout ?? TimeSpan.FromSeconds(120);
        _connectionMap = new TimeoutDictionary<string, UdpProxyWorker>(Timeout);
    }

    public ValueTask SendPacket(IPAddress sourceAddress, IPAddress destinationAddress, UdpPacket udpPacket, bool? noFragment)
    {
        var sourceEndPoint = new IPEndPoint(sourceAddress, udpPacket.SourcePort);
        var destinationEndPoint = new IPEndPoint(destinationAddress, udpPacket.DestinationPort);
        var addressFamily = destinationAddress.AddressFamily;
        Cleanup();

        // find the proxy for the connection (source-destination)
        var connectionKey = $"{sourceEndPoint}:{destinationEndPoint}";
        var udpWorker = _connectionMap.GetOrAdd(connectionKey, _ =>
        {
            // Find or create a worker that does not use the DestinationEndPoint
            lock (_udpWorkers)
            {
                var newUdpWorker = _udpWorkers.FirstOrDefault(x =>
                    x.AddressFamily == addressFamily &&
                    !x.DestinationEndPointMap.TryGetValue(destinationEndPoint, out var _));

                if (newUdpWorker == null)
                {
                    newUdpWorker = new UdpProxyWorker(this, _socketFactory.CreateUdpClient(addressFamily), addressFamily);
                    _udpWorkers.Add(newUdpWorker);
                }

                newUdpWorker.DestinationEndPointMap.TryAdd(destinationEndPoint, new TimeoutItem<IPEndPoint>(sourceEndPoint, false));
                return newUdpWorker;
            }
        });

        var dgram = udpPacket.PayloadData ?? Array.Empty<byte>();
        return udpWorker.SendPacket(destinationEndPoint, dgram, noFragment);
    }

    public void Cleanup()
    {
        // check clean up time
        var now = FastDateTime.Now;
        if (_lastCleanupTime + Timeout > now)
            return;
        _lastCleanupTime = now;

        // remove dead workers
        lock (_udpWorkers)
        {
            for (var i = _udpWorkers.Count - 1; i >= 0; i--)
            {
                var udpWorker = _udpWorkers[i];
                if (udpWorker.IsDisposed || udpWorker.AccessedTime < now - Timeout)
                {
                    udpWorker.Dispose();
                    _udpWorkers.RemoveAt(i);
                }
            }
        }
    }
}