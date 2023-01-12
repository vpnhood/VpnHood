using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using PacketDotNet;
using VpnHood.Common.Collections;
using VpnHood.Common.Logging;
using VpnHood.Common.Timing;

namespace VpnHood.Tunneling;

public class PingProxyPool : IDisposable, IWatchDog
{
    private readonly List<PingProxy> _pingProxies = new();
    private TimeSpan _icmpTimeout = TimeSpan.FromSeconds(120);

    public int WorkerMaxCount { get; set; }
    public int WorkerCount { get { lock (_pingProxies) return _pingProxies.Count; } }
    public WatchDogChecker WatchDogChecker { get; } = new(TimeSpan.FromMinutes(5));
    public TimeSpan WorkerTimeout { get; set; }= TimeSpan.FromMinutes(5);
    public TimeoutDictionary<IPEndPoint, TimeoutItem<bool>> RemoteEndPoints { get; } = new(TimeSpan.FromSeconds(30));
    public event EventHandler<EndPointEventArgs>? OnNewEndPoint;

    public TimeSpan IcmpTimeout
    {
        get => _icmpTimeout;
        set
        {
            _icmpTimeout = value;
            RemoteEndPoints.Timeout = value;
            WatchDogChecker.Interval = value;
        }
    }

    public PingProxyPool(int maxWorkerCount = 20)
    {
        WorkerMaxCount = maxWorkerCount > 0
            ? maxWorkerCount
            : throw new ArgumentException($"{nameof(maxWorkerCount)} must be greater than 0", nameof(maxWorkerCount));
    }

    private PingProxy GetFreePingProxy(out bool isNew)
    {
        lock (_pingProxies)
        {
            isNew = false;

            var pingProxy = _pingProxies.FirstOrDefault(x => !x.IsBusy);
            if (pingProxy != null)
                return pingProxy;

            if (_pingProxies.Count < WorkerMaxCount)
            {
                pingProxy = new PingProxy();
                _pingProxies.Add(pingProxy);
                isNew = true;
                return pingProxy;
            }

            pingProxy = _pingProxies.OrderBy(x => x.LastUsedTime).First();
            pingProxy.Cancel();
            return pingProxy;
        }
    }

    public Task<IPPacket> Send(IPPacket ipPacket)
    {
        var isNewRemoteEndPoint = false;
        bool isNewLocalEndPoint;
        Task<IPPacket> sendTask;

        // we know lock doesn't wait for async task, but wait till Send method to set its busy state before goes into its await
        lock (_pingProxies)
        {
            var pingProxy = GetFreePingProxy(out isNewLocalEndPoint) ?? throw new Exception($"{VhLogger.FormatTypeName(this)} needs more workers!");
            sendTask = pingProxy.Send(ipPacket);
        }

        // add the endpoint
        RemoteEndPoints.GetOrAdd(new IPEndPoint(ipPacket.DestinationAddress, 0), (_) =>
        {
            isNewRemoteEndPoint = true;
            return new TimeoutItem<bool>(true);
        });

        // raise new endpoint event
        if (isNewLocalEndPoint || isNewRemoteEndPoint)
            OnNewEndPoint?.Invoke(this, new EndPointEventArgs(ProtocolType.Icmp,
                new IPEndPoint(ipPacket.SourceAddress, 0), new IPEndPoint(ipPacket.DestinationAddress, 0),
                isNewLocalEndPoint, isNewRemoteEndPoint));

        return sendTask;
    }

    public Task DoWatch()
    {
        lock (_pingProxies)
            TimeoutItemUtil.CleanupTimeoutList(_pingProxies, WorkerTimeout);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_pingProxies)
            _pingProxies.ForEach(x => x.Dispose());
    }
}