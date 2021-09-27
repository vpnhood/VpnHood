using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PacketDotNet;

namespace VpnHood.Tunneling
{
    public class PingProxyPool : IDisposable
    {
        private readonly object _lockList = new();

        private readonly List<PingProxy> _pingProxies = new();
        private int MaxWorkerCount { get; }

        public PingProxyPool(int maxWorkerCount = 20)
        {
            MaxWorkerCount = maxWorkerCount > 0
                ? maxWorkerCount
                : throw new ArgumentException($"{nameof(maxWorkerCount)} must be greater than 0", nameof(maxWorkerCount));
        }

        private PingProxy GetFreePingProxy()
        {
            lock (_lockList)
            {
                var pingProxy = _pingProxies.FirstOrDefault(x => !x.IsBusy);
                if (pingProxy != null)
                    return pingProxy;

                if (_pingProxies.Count < MaxWorkerCount)
                {
                    pingProxy = new PingProxy();
                    _pingProxies.Add(pingProxy);
                    return pingProxy;
                }

                pingProxy = _pingProxies.OrderBy(x => x.SentTime).First();
                pingProxy.Cancel();
                return pingProxy;
            }
        }

        public Task<IPPacket> Send(IPPacket ipPacket)
        {
            lock (_lockList) // let Send method sets its busy flag
            {
                var pingProxy = GetFreePingProxy() ?? throw new Exception($"{nameof(PingProxyPool)} needs more workers!");
                return pingProxy.Send(ipPacket);
            }
        }

        public void Dispose()
        {
            foreach (var pingProxy in _pingProxies)
                pingProxy.Dispose();
        }
    }
}
