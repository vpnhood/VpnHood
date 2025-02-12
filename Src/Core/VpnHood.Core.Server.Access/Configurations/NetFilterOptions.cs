using System.Text.Json.Serialization;
using VpnHood.Core.Common.Net;
using VpnHood.Core.Common.Utils;

namespace VpnHood.Core.Server.Access.Configurations;

public class NetFilterOptions
{
    public bool? NetworkIsolation { get; set; }
    public bool? IncludeLocalNetwork { get; set; }
    public IpRange[]? VpnAdapterIncludeIpRanges { get; set; }
    public IpRange[]? VpnAdapterExcludeIpRanges { get; set; }
    public IpRange[]? IncludeIpRanges { get; set; }
    public IpRange[]? ExcludeIpRanges { get; set; }
    public bool? BlockIpV6 { get; set; }

    [JsonIgnore] public bool NetworkIsolationValue => NetworkIsolation ?? true;
    [JsonIgnore] public bool IncludeLocalNetworkValue => IncludeLocalNetwork ?? false;
    [JsonIgnore] public bool BlockIpV6Value => BlockIpV6 ?? false;

    public void Merge(NetFilterOptions obj)
    {
        if (obj.NetworkIsolation != null) NetworkIsolation = obj.NetworkIsolation;
        if (obj.IncludeLocalNetwork != null) IncludeLocalNetwork = obj.IncludeLocalNetwork;
        if (obj.VpnAdapterIncludeIpRanges != null) VpnAdapterIncludeIpRanges = obj.VpnAdapterIncludeIpRanges;
        if (obj.VpnAdapterExcludeIpRanges != null) VpnAdapterExcludeIpRanges = obj.VpnAdapterExcludeIpRanges;
        if (obj.IncludeIpRanges != null) IncludeIpRanges = obj.IncludeIpRanges;
        if (obj.ExcludeIpRanges != null) ExcludeIpRanges = obj.ExcludeIpRanges;
        if (obj.BlockIpV6 != null) BlockIpV6 = obj.BlockIpV6;
    }

    public void ApplyDefaults()
    {
        NetworkIsolation = NetworkIsolationValue;
        IncludeLocalNetwork = IncludeLocalNetworkValue;
        BlockIpV6 = BlockIpV6Value;
    }

    public IpRangeOrderedList GetFinalIncludeIpRanges()
    {
        var includeIpRanges = IpNetwork.All.ToIpRanges();
        if (!VhUtils.IsNullOrEmpty(IncludeIpRanges))
            includeIpRanges = includeIpRanges.Intersect(IncludeIpRanges);

        if (!VhUtils.IsNullOrEmpty(ExcludeIpRanges))
            includeIpRanges = includeIpRanges.Exclude(ExcludeIpRanges);

        return includeIpRanges;
    }

    public IEnumerable<IpRange> GetFinalVpnAdapterIncludeIpRanges()
    {
        var vpnAdapterIncludeIpRanges = IpNetwork.All.ToIpRanges();
        if (!IncludeLocalNetworkValue)
            vpnAdapterIncludeIpRanges = vpnAdapterIncludeIpRanges.Exclude(IpNetwork.LocalNetworks.ToIpRanges());

        if (!VhUtils.IsNullOrEmpty(VpnAdapterIncludeIpRanges))
            vpnAdapterIncludeIpRanges = vpnAdapterIncludeIpRanges.Intersect(VpnAdapterIncludeIpRanges);

        if (!VhUtils.IsNullOrEmpty(VpnAdapterExcludeIpRanges))
            vpnAdapterIncludeIpRanges = vpnAdapterIncludeIpRanges.Exclude(VpnAdapterExcludeIpRanges);

        return vpnAdapterIncludeIpRanges;
    }

    public IpRangeOrderedList GetBlockedIpRanges()
    {
        var includeIpRanges = GetFinalIncludeIpRanges().Intersect(GetFinalVpnAdapterIncludeIpRanges());
        if (BlockIpV6Value)
            includeIpRanges = includeIpRanges.Exclude(IpNetwork.AllV6.ToIpRange());

        return includeIpRanges.Invert();
    }
}