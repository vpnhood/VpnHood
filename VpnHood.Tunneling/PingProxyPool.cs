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

public class PingProxyPool : IDisposable, IJob
{
    private readonly List<PingProxy> _pingProxies = new();
    private TimeSpan _icmpTimeout = TimeSpan.FromSeconds(120);

    public int WorkerMaxCount { get; set; }
    public int WorkerCount { get { lock (_pingProxies) return _pingProxies.Count; } }
    public JobSection JobSection { get; } = new(TimeSpan.FromMinutes(5));
    public TimeSpan WorkerTimeout { get; set; }= TimeSpan.FromMinutes(5);
    public TimeoutDictionary<IPEndPoint, TimeoutItem<bool>> RemoteEndPoints { get; } = new(TimeSpan.FromSeconds(30));
    public event EventHandler<EndPointEventPairArgs>? OnEndPointEstablished;
    public event EventHandler<EndPointEventArgs>? OnNewRemoteEndPoint;

    public TimeSpan IcmpTimeout
    {
        get => _icmpTimeout;
        set
        {
            _icmpTimeout = value;
            RemoteEndPoints.Timeout = value;
            JobSection.Interval = value;
        }
    }

    public PingProxyPool(int maxWorkerCount = 20)
    {
        WorkerMaxCount = maxWorkerCount > 0
            ? maxWorkerCount
            : throw new ArgumentException($"{nameof(maxWorkerCount)} must be greater than 0", nameof(maxWorkerCount));

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
        var destinationEndPoint = new IPEndPoint(ipPacket.DestinationAddress, 0);
        bool isNewLocalEndPoint;
        var isNewRemoteEndPoint = false;

        // add the endpoint
        RemoteEndPoints.GetOrAdd(destinationEndPoint, (_) =>
        {
            isNewRemoteEndPoint = true;
            return new TimeoutItem<bool>(true);
        });
        if (isNewRemoteEndPoint)
            OnNewRemoteEndPoint?.Invoke(this, new EndPointEventArgs(ipPacket.Protocol, destinationEndPoint));

        // we know lock doesn't wait for async task, but wait till Send method to set its busy state before goes into its await
        Task<IPPacket> sendTask;
        lock (_pingProxies)
        {
            var pingProxy = GetFreePingProxy(out isNewLocalEndPoint) ?? throw new Exception($"{VhLogger.FormatTypeName(this)} needs more workers!");
            sendTask = pingProxy.Send(ipPacket);
        }

        // raise new endpoint event
        if (isNewLocalEndPoint || isNewRemoteEndPoint)
            OnEndPointEstablished?.Invoke(this, new EndPointEventPairArgs(ProtocolType.Icmp,
                new IPEndPoint(ipPacket.SourceAddress, 0), new IPEndPoint(ipPacket.DestinationAddress, 0),
                isNewLocalEndPoint, isNewRemoteEndPoint));

        return sendTask;
    }

    public Task RunJob()
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