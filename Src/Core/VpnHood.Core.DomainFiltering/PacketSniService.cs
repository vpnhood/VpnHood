using System.Collections.Concurrent;
using VpnHood.Core.DomainFiltering.SniExtractors;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.DomainFiltering;

/// <summary>
/// Get the packet and store the source endpoint result.
/// It uses its own TimeoutDictionary to store the result for next.
/// It uses IpEndPointValue to prevent heap allocation for each packet.
/// </summary>
public class PacketSniService(
    DomainFilterResolver domainFilterResolver, 
    IPacketSniExtractor sniExtractor,
    TimeSpan connectionTimeout)
{
    private readonly ConcurrentDictionary<IpEndPointValue, FlowInfo> _flowCache = new();
    private readonly Timer _cleanupTimer = new(CleanupExpiredFlows, null, connectionTimeout, connectionTimeout);

    public PacketFilterResult ProcessPacket(IpPacket ipPacket)
    {
        // Create flow key based on source endpoint
        var flowKey = CreateFlowKey(ipPacket);
        
        // Check if we already have a result for this flow
        if (_flowCache.TryGetValue(flowKey, out var flowInfo)) {
            // Update last seen time
            flowInfo.LastSeenTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;
            
            // If we already determined the action, return it with the current packet
            if (flowInfo.Action != DomainFilterAction.None) {
                return new PacketFilterResult(
                    flowInfo.Action, 
                    flowInfo.DomainName, 
                    [ipPacket]);
            }
        }

        // Extract SNI from packet
        var sniResult = sniExtractor.ExtractSni(ipPacket);

        // If we need more packets, buffer this one
        if (sniResult.NeedMore) {
            var newFlowInfo = flowInfo ?? new FlowInfo {
                LastSeenTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond
            };
            
            newFlowInfo.BufferedPackets.Add(ipPacket);
            _flowCache[flowKey] = newFlowInfo;
            
            return new PacketFilterResult(true);
        }

        // If SNI was found, resolve domain filter action
        if (sniResult.DomainName != null) {
            var action = domainFilterResolver.Process(sniResult.DomainName);
            
            // Cache the result for this flow
            var resultFlowInfo = new FlowInfo {
                DomainName = sniResult.DomainName,
                Action = action,
                LastSeenTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond
            };
            _flowCache[flowKey] = resultFlowInfo;
            
            // Return buffered packets plus current packet
            var packetsToReturn = new List<IpPacket>();
            if (flowInfo != null)
                packetsToReturn.AddRange(flowInfo.BufferedPackets);
            packetsToReturn.Add(ipPacket);
            
            return new PacketFilterResult(action, sniResult.DomainName, packetsToReturn);
        }

        // No SNI found and not buffering - just pass through
        var packets = new List<IpPacket>();
        if (flowInfo != null)
            packets.AddRange(flowInfo.BufferedPackets);
        packets.Add(ipPacket);
        
        return new PacketFilterResult(DomainFilterAction.None, null, packets);
    }

    private static IpEndPointValue CreateFlowKey(IpPacket ipPacket)
    {
        var port = ipPacket.PayloadPacket switch {
            UdpPacket udp => udp.SourcePort,
            TcpPacket tcp => tcp.SourcePort,
            _ => (ushort)0
        };

        return new IpEndPointValue(ipPacket.SourceAddress, port);
    }

    private static void CleanupExpiredFlows(object? state)
    {
        // This will be implemented to clean up expired flows
        // For now, it's a placeholder
    }

    private class FlowInfo
    {
        public string? DomainName { get; set; }
        public DomainFilterAction Action { get; set; }
        public long LastSeenTicks { get; set; }
        public List<IpPacket> BufferedPackets { get; } = new();
    }
}
