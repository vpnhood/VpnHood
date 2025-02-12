using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Net;
using VpnHood.Core.Common.Utils;

namespace VpnHood.Core.Server;

internal class VirtualIpManager(IpNetwork ipNetworkV4, IpNetwork ipNetworkV6, string lastVirtualIpFilePath)
{
    private readonly ConcurrentDictionary<IPAddress, Session> _virtualIps = new();
    private VirtualIpBundle _lastAllocatedVirtualIps =
        JsonUtils.TryDeserializeFile<VirtualIpBundle>(lastVirtualIpFilePath, logger: VhLogger.Instance) ?? new VirtualIpBundle {
            IpV4 = ipNetworkV4.FirstIpAddress,
            IpV6 = ipNetworkV6.FirstIpAddress
        };

    public IpNetwork IpNetworkV4 => ipNetworkV4;
    public IpNetwork IpNetworkV6 => ipNetworkV6;

    public VirtualIpBundle Allocate()
    {
        lock (_virtualIps) {
            // allocate new IPs
            _lastAllocatedVirtualIps = new VirtualIpBundle {
                IpV4 = Allocate(ipNetworkV4, _lastAllocatedVirtualIps.IpV4),
                IpV6 = Allocate(ipNetworkV6, _lastAllocatedVirtualIps.IpV6),
            };

            // save the last IPs
            File.WriteAllText(lastVirtualIpFilePath, JsonSerializer.Serialize(_lastAllocatedVirtualIps));
            return _lastAllocatedVirtualIps;
        }
    }

    private IPAddress Allocate(IpNetwork ipNetwork, IPAddress lastUsedIpAddress)
    {
        var firstValidIpAddress = IPAddressUtil.Increment(IPAddressUtil.Increment(ipNetwork.FirstIpAddress));

        var ipAddress = IPAddressUtil.Compare(lastUsedIpAddress, firstValidIpAddress) < 0
            ? firstValidIpAddress
            : lastUsedIpAddress;

        for (var i = 0; i < 0xffff; i++) {
            if (ipAddress.Equals(ipNetwork.LastIpAddress))
                ipAddress = firstValidIpAddress;

            if (!_virtualIps.ContainsKey(ipAddress))
                return ipAddress;

            ipAddress = IPAddressUtil.Increment(ipAddress);
        }

        throw new Exception("Could not allocate a new virtual IP.");
    }

    public void Add(VirtualIpBundle virtualIpBundle, Session session)
    {
        if (!_virtualIps.TryAdd(virtualIpBundle.IpV4, session))
            throw new SessionException(SessionErrorCode.SessionError,
                $"Could not add virtual IPv4 to collection. IpAddress: {virtualIpBundle.IpV4}");

        if (!_virtualIps.TryAdd(virtualIpBundle.IpV6, session))
            throw new SessionException(SessionErrorCode.SessionError,
                $"Could not add virtual IPv6 to collection. IpAddress: {virtualIpBundle.IpV6}");
    }

    public void Release(VirtualIpBundle virtualIps)
    {
        _virtualIps.TryRemove(virtualIps.IpV4, out _);
        _virtualIps.TryRemove(virtualIps.IpV6, out _);
    }

    public Session? FindSession(IPAddress virtualIpAddress)
    {
        return _virtualIps.GetValueOrDefault(virtualIpAddress);
    }
}
