using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Server;

internal class VirtualIpManager(IpNetwork ipNetworkV4, IpNetwork ipNetworkV6, string lastVirtualIpFilePath)
{
    // keyed by value, as the adapter looks up each packet's destination
    private readonly ConcurrentDictionary<IpAddressValue, Session> _virtualIps = new();
    private readonly AsyncLock _allocateLock = new();

    // read at the first allocation, its only use
    private VirtualIpBundle? _lastAllocatedVirtualIps;

    public IpNetwork IpNetworkV4 => ipNetworkV4;
    public IpNetwork IpNetworkV6 => ipNetworkV6;

    public async Task<VirtualIpBundle> Allocate(CancellationToken cancellationToken)
    {
        using var lockResult = await _allocateLock.LockAsync(cancellationToken).Vhc();
        var lastVirtualIps = _lastAllocatedVirtualIps ??=
            await JsonUtils.TryDeserializeFileAsync<VirtualIpBundle>(lastVirtualIpFilePath, cancellationToken).Vhc() ??
            new VirtualIpBundle {
                IpV4 = ipNetworkV4.FirstIpAddress,
                IpV6 = ipNetworkV6.FirstIpAddress
            };

        // allocate new IPs
        var virtualIps = new VirtualIpBundle {
            IpV4 = Allocate(ipNetworkV4, lastVirtualIps.IpV4),
            IpV6 = Allocate(ipNetworkV6, lastVirtualIps.IpV6)
        };

        // saved before they count as allocated: a failed save leaves them to the next allocation
        await FileUtils.WriteAllTextAtomicAsync(lastVirtualIpFilePath, JsonSerializer.Serialize(virtualIps),
            cancellationToken).Vhc();
        _lastAllocatedVirtualIps = virtualIps;
        return virtualIps;
    }

    private IPAddress Allocate(IpNetwork ipNetwork, IPAddress lastUsedIpAddress)
    {
        var firstValidIpAddress = IPAddressUtil.Increment(IPAddressUtil.Increment(ipNetwork.FirstIpAddress));

        // start after the last allocation, which its session may not have added yet: sessions created at once never
        // get the same IPs
        var ipAddress = IPAddressUtil.Compare(lastUsedIpAddress, firstValidIpAddress) < 0
            ? firstValidIpAddress
            : IPAddressUtil.Increment(lastUsedIpAddress);

        for (var i = 0; i < 0xffff; i++) {
            if (ipAddress.Equals(ipNetwork.LastIpAddress))
                ipAddress = firstValidIpAddress;

            if (!_virtualIps.ContainsKey(ipAddress.ToValue()))
                return ipAddress;

            ipAddress = IPAddressUtil.Increment(ipAddress);
        }

        throw new Exception("Could not allocate a new virtual IP.");
    }

    // the session manager adds a session's IPs as it builds it and releases them as it disposes it; a failed add
    // leaves nothing behind
    public void Add(VirtualIpBundle virtualIpBundle, Session session)
    {
        if (!_virtualIps.TryAdd(virtualIpBundle.IpV4.ToValue(), session))
            throw new SessionException(SessionErrorCode.SessionError,
                $"Could not add virtual IPv4 to collection. IpAddress: {virtualIpBundle.IpV4}");

        if (!_virtualIps.TryAdd(virtualIpBundle.IpV6.ToValue(), session)) {
            _virtualIps.TryRemove(KeyValuePair.Create(virtualIpBundle.IpV4.ToValue(), session));
            throw new SessionException(SessionErrorCode.SessionError,
                $"Could not add virtual IPv6 to collection. IpAddress: {virtualIpBundle.IpV6}");
        }
    }

    // frees only the IPs the session still holds: a session that took them since keeps them
    public void Release(VirtualIpBundle virtualIps, Session session)
    {
        _virtualIps.TryRemove(KeyValuePair.Create(virtualIps.IpV4.ToValue(), session));
        _virtualIps.TryRemove(KeyValuePair.Create(virtualIps.IpV6.ToValue(), session));
    }

    public Session? FindSession(IpAddressValue virtualIpAddress)
    {
        return _virtualIps.GetValueOrDefault(virtualIpAddress);
    }
}