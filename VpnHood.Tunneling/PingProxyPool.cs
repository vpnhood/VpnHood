using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using PacketDotNet;
using VpnHood.Common.Collections;
using VpnHood.Common.JobController;
using VpnHood.Common.Logging;

namespace VpnHood.Tunneling;

public class PingProxyPool : IPacketProxyPool, IJob
{
    private readonly IPacketProxyReceiver _packetProxyReceiver;
    private readonly List<PingProxy> _pingProxies = new();
    private readonly EventReporter _maxWorkerEventReporter;
    private readonly TimeoutDictionary<IPEndPoint, TimeoutItem<bool>> _remoteEndPoints;
    private readonly TimeSpan _workerTimeout = TimeSpan.FromMinutes(5);
    private readonly int _workerMaxCount;

    public int ClientCount { get { lock (_pingProxies) return _pingProxies.Count; } }
    public int RemoteEndPointCount => _remoteEndPoints.Count;
    public JobSection JobSection { get; } = new(TimeSpan.FromMinutes(5));

    public PingProxyPool(IPacketProxyReceiver packetProxyReceiver, TimeSpan? icmpTimeout = null,
        int maxWorkerCount = 20, LogScope? logScope = null)
    {
        _workerMaxCount = (maxWorkerCount > 0) ? maxWorkerCount : throw new ArgumentException($"{nameof(maxWorkerCount)} must be greater than 0", nameof(maxWorkerCount));
        _packetProxyReceiver = packetProxyReceiver;
        _remoteEndPoints = new TimeoutDictionary<IPEndPoint, TimeoutItem<bool>>(icmpTimeout ?? TimeSpan.FromMilliseconds(120));
        _maxWorkerEventReporter = new EventReporter(VhLogger.Instance, "Session has reached to the maximum ping workers.", logScope: logScope);

        JobRunner.Default.Add(this);
    }

    private PingProxy GetFreePingProxy(out bool isNew)
    {
        lock (_pingProxies)
        {
            isNew = false;

            var pingProxy = _pingProxies.FirstOrDefault(x => !x.IsBusy);
            if (pingProxy != null)
                return pingProxy;

            if (_pingProxies.Count < _workerMaxCount)
            {
                pingProxy = new PingProxy();
                _pingProxies.Add(pingProxy);
                isNew = true;
                return pingProxy;
            }

            _maxWorkerEventReporter.Raised();

            pingProxy = _pingProxies.OrderBy(x => x.LastUsedTime).First();
            pingProxy.Cancel();
            return pingProxy;
        }
    }

    public async Task SendPacket(IPPacket ipPacket)
    {
        if ((ipPacket.Version != IPVersion.IPv4 || ipPacket.Extract<IcmpV4Packet>()?.TypeCode != IcmpV4TypeCode.EchoRequest) &&
            (ipPacket.Version != IPVersion.IPv6 || ipPacket.Extract<IcmpV6Packet>()?.Type != IcmpV6Type.EchoRequest))
            throw new NotSupportedException($"The icmp is not supported. Packet: {PacketUtil.Format(ipPacket)}.");

        var destinationEndPoint = new IPEndPoint(ipPacket.DestinationAddress, 0);
        bool isNewLocalEndPoint;
        var isNewRemoteEndPoint = false;

        // add the endpoint
        _remoteEndPoints.GetOrAdd(destinationEndPoint, (_) =>
        {
            isNewRemoteEndPoint = true;
            return new TimeoutItem<bool>(true);
        });
        if (isNewRemoteEndPoint)
            _packetProxyReceiver.OnNewRemoteEndPoint(ipPacket.Protocol, destinationEndPoint);

        // we know lock doesn't wait for async task, but wait till Send method to set its busy state before goes into its await
        Task<IPPacket> sendTask;
        lock (_pingProxies)
        {
            var pingProxy = GetFreePingProxy(out isNewLocalEndPoint);
            sendTask = pingProxy.Send(ipPacket);
        }

        // raise new endpoint event
        if (isNewLocalEndPoint || isNewRemoteEndPoint)
            _packetProxyReceiver.OnNewEndPoint(ipPacket.Protocol,
                new IPEndPoint(ipPacket.SourceAddress, 0), new IPEndPoint(ipPacket.DestinationAddress, 0),
                isNewLocalEndPoint, isNewRemoteEndPoint);

        var result = await sendTask;
        await _packetProxyReceiver.OnPacketReceived(result);
    }

    public Task RunJob()
    {
        lock (_pingProxies)
            TimeoutItemUtil.CleanupTimeoutList(_pingProxies, _workerTimeout);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_pingProxies)
            _pingProxies.ForEach(x => x.Dispose());
        _maxWorkerEventReporter.Dispose();
    }
}