using System.Text.Json.Serialization;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;

namespace VpnHood.Server.Access.Configurations;

public class NetFilterOptions
{
    public bool? IncludeLocalNetwork { get; set; }
    public IpRange[]? PacketCaptureIncludeIpRanges { get; set; }
    public IpRange[]? PacketCaptureExcludeIpRanges { get; set; }
    public IpRange[]? IncludeIpRanges { get; set; }
    public IpRange[]? ExcludeIpRanges { get; set; }
    public bool? BlockIpV6 { get; set; }

    [JsonIgnore] public bool IncludeLocalNetworkValue => IncludeLocalNetwork ?? false;
    [JsonIgnore] public bool BlockIpV6Value => BlockIpV6 ?? false;

    public void Merge(NetFilterOptions obj)
    {
        if (obj.IncludeLocalNetwork != null) IncludeLocalNetwork = obj.IncludeLocalNetwork;
        if (obj.PacketCaptureIncludeIpRanges != null) PacketCaptureIncludeIpRanges = obj.PacketCaptureIncludeIpRanges;
        if (obj.PacketCaptureExcludeIpRanges != null) PacketCaptureExcludeIpRanges = obj.PacketCaptureExcludeIpRanges;
        if (obj.IncludeIpRanges != null) IncludeIpRanges = obj.IncludeIpRanges;
        if (obj.ExcludeIpRanges != null) ExcludeIpRanges = obj.ExcludeIpRanges;
        if (obj.BlockIpV6 != null) BlockIpV6 = obj.BlockIpV6;
    }
    public void ApplyDefaults()
    {
        IncludeLocalNetwork = IncludeLocalNetworkValue;
        BlockIpV6 = BlockIpV6Value;
    }

    public IpRangeOrderedList GetFinalIncludeIpRanges()
    {
        var includeIpRanges = IpNetwork.All.ToIpRangesNew();
        if (!VhUtil.IsNullOrEmpty(IncludeIpRanges))
            includeIpRanges = includeIpRanges.IntersectNew(IncludeIpRanges);

        if (!VhUtil.IsNullOrEmpty(ExcludeIpRanges))
            includeIpRanges = includeIpRanges.ExcludeNew(ExcludeIpRanges);

        return includeIpRanges;
    }

    public IEnumerable<IpRange> GetFinalPacketCaptureIncludeIpRanges()
    {
        var packetCaptureIncludeIpRanges = IpNetwork.All.ToIpRanges();
        if (!IncludeLocalNetworkValue)
            packetCaptureIncludeIpRanges = packetCaptureIncludeIpRanges.ExcludeNew(IpNetwork.LocalNetworks.ToIpRanges());

        if (!VhUtil.IsNullOrEmpty(PacketCaptureIncludeIpRanges))
            packetCaptureIncludeIpRanges = packetCaptureIncludeIpRanges.IntersectNew(PacketCaptureIncludeIpRanges);

        if (!VhUtil.IsNullOrEmpty(PacketCaptureExcludeIpRanges))
            packetCaptureIncludeIpRanges = packetCaptureIncludeIpRanges.ExcludeNew(PacketCaptureExcludeIpRanges);

        return packetCaptureIncludeIpRanges;
    }

    public IpRangeOrderedList GetBlockedIpRanges()
    {
        var includeIpRanges = GetFinalIncludeIpRanges().IntersectNew(GetFinalPacketCaptureIncludeIpRanges());
        if (BlockIpV6Value)
            includeIpRanges = includeIpRanges.ExcludeNew(IpNetwork.AllV6.ToIpRange());

        return includeIpRanges.InvertNew();
    }

}