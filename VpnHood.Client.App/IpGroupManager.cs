using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App;

public class IpGroupManager
{
    private readonly ZipArchive _zipArchive;
    private string[]? _ipGroupIds;
    private readonly Dictionary<string, IpRangeOrderedList> _ipGroupIpRanges = new();

    private IpGroupManager(ZipArchive zipArchive)
    {
        _zipArchive = zipArchive;
    }


    public static Task<IpGroupManager> Create(ZipArchive zipArchive)
    {
        var ret = new IpGroupManager(zipArchive);
        return Task.FromResult(ret);
    }

    public Task<string[]> GetIpGroupIds()
    {
        _ipGroupIds ??= _zipArchive.Entries
            .Where(x=>Path.GetExtension(x.Name)==".ips")
            .Select(x=>Path.GetFileNameWithoutExtension(x.Name))
            .ToArray();

        return Task.FromResult(_ipGroupIds);
    }

    public async Task<IpRangeOrderedList> GetIpRanges(string ipGroupId)
    {
        var ipRanges = await GetIpRangesInternal(ipGroupId).VhConfigureAwait();
        _ipGroupIpRanges.TryAdd(ipGroupId, ipRanges);
        return ipRanges;
    }

    private async Task<IpRangeOrderedList> GetIpRangesInternal(string ipGroupId)
    {
        if (_ipGroupIpRanges.TryGetValue(ipGroupId, out var ipGroupRangeCache))
            return ipGroupRangeCache;

        try
        {
            await using var stream = _zipArchive.GetEntry($"{ipGroupId.ToLower()}.ips")?.Open() ?? throw new NotExistsException();
            return IpRangeOrderedList.Deserialize(stream);
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not load ip ranges for {IpGroupId}", ipGroupId);
            return IpRangeOrderedList.Empty;
        }
    }

    public async Task<IpGroup> GetIpGroup(IPAddress ipAddress, string? lastIpGroupId)
    {
        return await FindIpGroup(ipAddress, lastIpGroupId).VhConfigureAwait() 
               ?? throw new NotExistsException($"Could not find any ip group for the given ip. IP: {VhLogger.Format(ipAddress)}");
    }

    public async Task<IpGroup?> FindIpGroup(IPAddress ipAddress, string? lastIpGroupId)
    {
        // IpGroup
        if (lastIpGroupId != null)
        {
            var ipRanges = await GetIpRanges(lastIpGroupId).VhConfigureAwait();
            if (ipRanges.Any(x => x.IsInRange(ipAddress)))
            {
                _ipGroupIpRanges.TryAdd(lastIpGroupId, ipRanges);
                return new IpGroup
                {
                    IpGroupId = lastIpGroupId,
                    IpRanges = ipRanges
                };
            }
        }

        // iterate through all groups
        var ipGroupIds = await GetIpGroupIds();
        foreach (var ipGroupId in ipGroupIds)
        {
            var ipRanges = await GetIpRanges(ipGroupId).VhConfigureAwait();
            if (ipRanges.Any(x => x.IsInRange(ipAddress)))
            {
                _ipGroupIpRanges.TryAdd(ipGroupId, ipRanges);
                return new IpGroup
                {
                    IpGroupId = ipGroupId,
                    IpRanges = ipRanges
                };
            }
        }

        return null;
    }

    public async Task<string?> GetCountryCodeByCurrentIp()
    {
        try
        {
            var ipAddress =
                await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetwork).VhConfigureAwait() ??
                await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetworkV6).VhConfigureAwait();

            if (ipAddress == null)
                return null;

            var ipGroup = await FindIpGroup(ipAddress, null).VhConfigureAwait();
            return ipGroup?.IpGroupId;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not retrieve client country from public ip services.");
            return null;
        }
    }
}